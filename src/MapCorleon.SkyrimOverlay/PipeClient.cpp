#include "PipeClient.h"

#include <Windows.h>
#include <nlohmann/json.hpp>

#include <array>
#include <algorithm>
#include <thread>

namespace
{
    using namespace std::chrono_literals;
    using Json = nlohmann::json;

    struct Handle
    {
        HANDLE value;
        ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
        Handle(const Handle&) = delete;
        Handle& operator=(const Handle&) = delete;
        explicit Handle(HANDLE handle) : value(handle) {}
    };

    template <class Start>
    bool Transfer(HANDLE pipe, Start start, DWORD& transferred)
    {
        Handle ready(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        if (!ready.value) return false;
        OVERLAPPED operation{};
        operation.hEvent = ready.value;
        if (!start(&operation))
        {
            if (GetLastError() != ERROR_IO_PENDING) return false;
            if (WaitForSingleObject(ready.value, 1000) != WAIT_OBJECT_0)
            {
                CancelIoEx(pipe, &operation);
                GetOverlappedResult(pipe, &operation, &transferred, TRUE);
                return false;
            }
        }
        return GetOverlappedResult(pipe, &operation, &transferred, FALSE) != FALSE;
    }

    HANDLE OpenPipe()
    {
        // MapCorleon creates a new pipe instance after each response.
        for (int attempt = 0; attempt < 3; ++attempt)
        {
            if (attempt > 0) std::this_thread::sleep_for(20ms);
            const auto pipe = CreateFileW(LR"(\\.\pipe\MapCorleon.SkyrimOverlay.v1)", GENERIC_READ | GENERIC_WRITE,
                0, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
            if (pipe != INVALID_HANDLE_VALUE) return pipe;
            const auto error = GetLastError();
            if (error != ERROR_PIPE_BUSY && error != ERROR_FILE_NOT_FOUND) break;
        }
        return INVALID_HANDLE_VALUE;
    }

    OverlaySnapshot Exchange(const Json& request)
    {
        Handle pipe(OpenPipe());
        if (pipe.value == INVALID_HANDLE_VALUE) return {};

        DWORD mode = PIPE_READMODE_MESSAGE;
        if (!SetNamedPipeHandleState(pipe.value, &mode, nullptr, nullptr)) return {};
        auto message = request.dump();
        DWORD size = 0;
        if (!Transfer(pipe.value, [&](OVERLAPPED* operation) {
            return WriteFile(pipe.value, message.data(), static_cast<DWORD>(message.size()), nullptr, operation);
        }, size) || size != message.size()) return {};

        std::array<char, 16384> buffer{};
        if (!Transfer(pipe.value, [&](OVERLAPPED* operation) {
            return ReadFile(pipe.value, buffer.data(), static_cast<DWORD>(buffer.size()), nullptr, operation);
        }, size)) return {};

        auto response = Json::parse(buffer.begin(), buffer.begin() + size, nullptr, false);
        if (!response.is_object()) return {};
        OverlaySnapshot snapshot;
        snapshot.scope = response.at("scope").get<std::string>();
        snapshot.canFollow = response.at("canFollow").get<bool>();
        for (const auto& card : response.at("cards"))
        {
            snapshot.cards.push_back({ card.at("id").get<int>(), card.at("title").get<std::string>(),
                card.at("category").get<std::string>() });
        }
        if (auto feedback = response.find("feedback"); feedback != response.end() && feedback->is_string())
            snapshot.feedback = feedback->get<std::string>();
        return snapshot;
    }
}

void PipeClient::Start()
{
    // SKSE keeps successful plugins loaded for the lifetime of the game.
    std::thread([this] { Run(); }).detach();
}

OverlaySnapshot PipeClient::Snapshot()
{
    std::lock_guard lock(mutex);
    auto snapshot = current;
    if (std::chrono::steady_clock::now() >= feedbackUntil) snapshot.feedback.clear();
    return snapshot;
}

OverlaySnapshot PipeClient::ToastSnapshot()
{
    const auto now = Clock::now();
    if (now >= toastUntil.load()) return {};

    std::lock_guard lock(mutex);
    OverlaySnapshot snapshot;
    if (now < nearbyToastUntil) snapshot.cards = nearbyToast;
    if (showFollowFeedback && now < feedbackUntil) snapshot.feedback = current.feedback;
    return snapshot;
}

void PipeClient::SetPanelOpen(bool open)
{
    {
        std::lock_guard lock(mutex);
        panelOpen = open;
        nearbyToast.clear();
        nearbyToastUntil = {};
    }
    changed.notify_one();
}

bool PipeClient::Send(const char* action, const std::string& scope, int locationId)
{
    {
        std::lock_guard lock(mutex);
        if (actionPending) return false;
        if (scope.empty() ? std::string_view(action) != "toggleFollow" : !current.canFollow || current.scope != scope)
            return false;
        actionPending = true;
        queued = Request{ action, scope, locationId };
    }
    changed.notify_one();
    return true;
}

void PipeClient::UpdateNearbyToast(const OverlaySnapshot& next, Clock::time_point now)
{
    // A card can fall out of the closest three briefly without the player leaving.
    std::erase_if(recentlyNearby, [&](const auto& entry) { return now - entry.second >= 10s; });
    if (next.scope.empty()) return;
    if (next.scope != nearbyScope)
    {
        nearbyScope = next.scope;
        recentlyNearby.clear();
        nearbyToast.clear();
    }

    std::vector<OverlayCard> arrivals;
    for (const auto& card : next.cards)
    {
        const auto inserted = recentlyNearby.insert_or_assign(card.id, now).second;
        if (inserted && !panelOpen) arrivals.push_back(card);
    }

    if (!arrivals.empty())
    {
        nearbyToast = std::move(arrivals);
        nearbyToastUntil = now + 4s;
    }
    else if (now >= nearbyToastUntil || !next.canFollow)
        nearbyToast.clear();
}

void PipeClient::Run()
{
    while (true)
    {
        std::optional<Request> request;
        bool wasOpen;
        {
            std::lock_guard lock(mutex);
            request.swap(queued);
            wasOpen = panelOpen;
        }

        OverlaySnapshot next;
        try
        {
            if (!request || request->scope.empty())
                next = Exchange({ { "action", "state" } });

            if (request)
            {
                const auto& scope = request->scope.empty() ? next.scope : request->scope;
                if (!scope.empty() && (!request->scope.empty() || next.canFollow))
                    next = Exchange({ { "action", request->action }, { "scope", scope }, { "locationId", request->locationId } });
            }
        }
        catch (const Json::exception&) { }

        {
            std::unique_lock lock(mutex);
            const auto now = Clock::now();
            UpdateNearbyToast(next, now);
            if (!next.feedback.empty())
            {
                feedbackUntil = now + 2s;
                showFollowFeedback = request && request->action == "toggleFollow";
            }
            else if (next.scope == current.scope)
                next.feedback = current.feedback;
            current = std::move(next);
            toastUntil.store(std::max(nearbyToast.empty() ? Clock::time_point{} : nearbyToastUntil,
                showFollowFeedback && !current.feedback.empty() ? feedbackUntil : Clock::time_point{}));
            if (request) actionPending = false;
            changed.wait_for(lock, panelOpen ? 300ms : 1200ms,
                [&] { return panelOpen != wasOpen || queued.has_value(); });
        }
    }
}

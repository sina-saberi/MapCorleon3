#pragma once

#include <chrono>
#include <atomic>
#include <condition_variable>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

struct OverlayCard
{
    int id;
    std::string title;
    std::string category;
};

struct OverlaySnapshot
{
    std::string scope;
    bool canFollow = false;
    std::vector<OverlayCard> cards;
    std::string feedback;
};

class PipeClient
{
public:
    void Start();
    void SetPanelOpen(bool open);
    OverlaySnapshot Snapshot();
    OverlaySnapshot ToastSnapshot();
    bool Send(const char* action, const std::string& scope, int locationId = 0);

private:
    using Clock = std::chrono::steady_clock;
    void Run();
    void UpdateNearbyToast(const OverlaySnapshot& next, Clock::time_point now);

    struct Request
    {
        std::string action;
        std::string scope;
        int locationId;
    };

    std::mutex mutex;
    std::condition_variable changed;
    std::optional<Request> queued;
    bool actionPending = false;
    bool panelOpen = false;
    OverlaySnapshot current;
    Clock::time_point feedbackUntil;
    Clock::time_point nearbyToastUntil;
    std::atomic<Clock::time_point> toastUntil{};
    bool showFollowFeedback = false;
    std::string nearbyScope;
    std::unordered_map<int, Clock::time_point> recentlyNearby;
    std::vector<OverlayCard> nearbyToast;
};

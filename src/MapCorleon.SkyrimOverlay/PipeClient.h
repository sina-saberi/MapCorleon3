#pragma once

#include <chrono>
#include <atomic>
#include <condition_variable>
#include <cstdint>
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

struct OverlayKeys
{
    std::uint8_t panel;
    std::uint8_t follow;
};

struct OverlaySnapshot
{
    std::string scope;
    bool canFollow = false;
    std::vector<OverlayCard> cards;
    std::string feedback;
    std::optional<OverlayKeys> keys;
    std::chrono::seconds nearbyNotificationDuration{ 4 };
};

class PipeClient
{
public:
    explicit PipeClient(OverlayKeys iniKeys);
    OverlayKeys Keys() const;
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
    const OverlayKeys iniKeys;
    std::atomic<OverlayKeys> activeKeys;
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

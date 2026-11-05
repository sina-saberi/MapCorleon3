#include "PipeClient.h"

#include <RE/Skyrim.h>
#include <SKSE/SKSE.h>
#include <SKSEMenuFramework.h>

#include <array>
#include <fstream>
#include <utility>

namespace
{
    constexpr REL::Version SkyrimRuntime{ 1, 6, 1170, 0 };
}

SKSEPluginInfo(
    .Version = REL::Version{ 0, 1, 0, 0 },
    .Name = std::string_view{ "MapCorleonOverlay" },
    .RuntimeCompatibility = { SkyrimRuntime },
    .MinimumSKSEVersion = REL::Version{ 2, 2, 6, 0 }
)

namespace
{
    constexpr auto SettingsPath = L".\\Data\\SKSE\\Plugins\\MapCorleonOverlay.ini";
    constexpr unsigned int Escape = 0x01;
    unsigned int overlayKey = 0x41; // F7
    unsigned int followKey = 0x42;  // F8
    SKSEMenuFramework::Model::WindowInterface* controls;
    PipeClient* client;
    std::array<bool, 256> consumedKeys{};

    void Log(std::string_view message)
    {
        if (const auto directory = SKSE::log::log_directory())
            std::ofstream(*directory / "MapCorleonOverlay.log", std::ios::app) << message << '\n';
    }

    unsigned int ReadKey(const wchar_t* name, unsigned int defaultKey)
    {
        const auto key = GetPrivateProfileIntW(L"Keys", name, defaultKey, SettingsPath);
        return key > Escape && key < 256 ? key : defaultKey;
    }

    void SetPanelOpen(bool open)
    {
        controls->IsOpen = open;
        client->SetPanelOpen(open);
    }

    void DrawPanel(const OverlaySnapshot& snapshot, bool interactive)
    {
        using namespace ImGuiMCP;
        constexpr auto flags = ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove |
            ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoTitleBar |
            ImGuiWindowFlags_AlwaysAutoResize;
        const auto width = GetIO()->DisplaySize.x;
        SetNextWindowPos({ width - 22.0f, 65.0f }, ImGuiCond_Always, { 1.0f, 0.0f });
        SetNextWindowSize({ 320.0f, 0.0f }, ImGuiCond_Always);
        if (Begin("MapCorleon##Nearby", nullptr, flags |
            (interactive ? 0 : ImGuiWindowFlags_NoInputs | ImGuiWindowFlags_NoFocusOnAppearing)))
        {
            if (interactive)
            {
                TextUnformatted("MapCorleon | Nearby");
                SameLine();
                if (SmallButton("Close")) SetPanelOpen(false);
                if (snapshot.cards.empty())
                    TextUnformatted(snapshot.canFollow ? "No nearby locations" : "Player location unavailable");
            }
            if (!snapshot.feedback.empty()) TextUnformatted(snapshot.feedback.c_str());
            for (const auto& card : snapshot.cards)
            {
                PushID(card.id);
                if (interactive) Separator();
                PushTextWrapPos(302.0f);
                TextUnformatted(card.title.c_str());
                if (!card.category.empty()) TextUnformatted(card.category.c_str());
                PopTextWrapPos();
                if (interactive)
                {
                    if (SmallButton("Collect")) client->Send("collect", snapshot.scope, card.id);
                    SameLine();
                    if (SmallButton("X")) client->Send("dismiss", snapshot.scope, card.id);
                }
                PopID();
            }
        }
        End();
    }

    void __stdcall DrawControls()
    {
        DrawPanel(client->Snapshot(), true);
    }

    void __stdcall DrawNotifications()
    {
        if (controls->IsOpen) return;
        const auto snapshot = client->ToastSnapshot();
        if (snapshot.cards.empty() && snapshot.feedback.empty()) return;
        if (!SKSEMenuFramework::IsAnyBlockingWindowOpened()) DrawPanel(snapshot, false);
    }

    bool __stdcall OnInput(RE::InputEvent* event)
    {
        if (event->GetDevice() != RE::INPUT_DEVICE::kKeyboard) return false;
        const auto button = event->AsButtonEvent();
        if (!button) return false;
        const auto key = button->GetIDCode();
        if (key != overlayKey && key != followKey && key != Escape) return false;
        if (!button->IsDown())
            return button->IsPressed() ? consumedKeys[key] : std::exchange(consumedKeys[key], false);

        if (key == Escape || key == overlayKey)
        {
            if (controls->IsOpen)
            {
                SetPanelOpen(false);
                return consumedKeys[key] = true;
            }
            if (key == Escape || SKSEMenuFramework::IsAnyBlockingWindowOpened()) return false;
            SetPanelOpen(true);
            return consumedKeys[key] = true;
        }

        if (!controls->IsOpen && SKSEMenuFramework::IsAnyBlockingWindowOpened()) return false;
        client->Send("toggleFollow", {});
        return consumedKeys[key] = true;
    }

    void OnMessage(SKSE::MessagingInterface::Message* message)
    {
        if (message->type != SKSE::MessagingInterface::kDataLoaded) return;
        const auto framework = GetMenuFrameworkModule();
        constexpr const char* exports[] = {
            "AddWindow", "RegisterHudElement", "RegisterInpoutEvent", "IsAnyBlockingWindowOpened",
            "igGetIO", "igSetNextWindowPos", "igSetNextWindowSize", "igBegin", "igEnd", "igTextUnformatted",
            "igSameLine", "igSmallButton", "igPushID_Int", "igPopID", "igSeparator", "igPushTextWrapPos", "igPopTextWrapPos"
        };
        if (!framework)
        {
            Log("SKSE Menu Framework is missing. Install a compatible release and restart Skyrim.");
            return;
        }
        for (const auto name : exports)
        {
            if (GetProcAddress(framework, name)) continue;
            Log(std::string("SKSE Menu Framework is missing API export ") + name);
            return;
        }

        overlayKey = ReadKey(L"OverlayScanCode", overlayKey);
        followKey = ReadKey(L"FollowScanCode", followKey);
        if (overlayKey == followKey)
        {
            overlayKey = 0x41;
            followKey = 0x42;
        }
        controls = SKSEMenuFramework::AddWindow(DrawControls);
        if (!controls)
        {
            Log("SKSE Menu Framework did not create the overlay window.");
            return;
        }
        client = new PipeClient;
        client->Start();
        SKSEMenuFramework::AddHudElement(DrawNotifications);
        SKSEMenuFramework::AddInputEvent(OnInput);
        Log("Overlay registered. Waiting for MapCorleon desktop.");
    }
}

SKSEPluginLoad(const SKSE::LoadInterface* skse)
{
    if (skse->RuntimeVersion() != SkyrimRuntime) return false;
    SKSE::Init(skse);
    Log("MapCorleon overlay DLL loaded.");
    return SKSE::GetMessagingInterface()->RegisterListener(OnMessage);
}

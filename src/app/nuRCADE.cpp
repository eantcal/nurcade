// This file is part of nuRCADE (New (nu) Raycasting Classic Arcade Development Engine).
// Copyright (C) 2005 - 2018
// Antonino Calderone (antonino.calderone@gmail.com)
// All rights reserved.  
// Licensed under the MIT License. 
// See COPYING file in the project root for full license information.


/* -------------------------------------------------------------------------- */

#define _CRT_SECURE_NO_DEPRECATE

#include <windows.h>
#include "DdxDevice.h"

#include <memory>
#include <string>
#include <type_traits>
#include <vector>


/* -------------------------------------------------------------------------- */

using namespace std;


/* -------------------------------------------------------------------------- */

static void DbgTrace(HWND hWnd, LPCTSTR szError, ...);
static HRESULT InitInstance(HINSTANCE hInstance, int nCmdShow);


/* -------------------------------------------------------------------------- */

#include <stdio.h>
#include <algorithm>
#include <cctype>
#include <chrono>
#include <cmath>
#include <deque>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <functional>
#include <limits>
#include <map>
#include <nlohmann/json.hpp>
#include "nuRCADE-resource.h"
#include "ActorSystem.h"
#include "BackgroundMusicPlayer.h"
#include "RayCaster.h"
#include "RaycastEngine.h"
#include "SceneLoader.h"
#include "SoundEffectPlayer.h"
#include "SpriteMetadataLoader.h"
#include "SpriteSet.h"
#include "TextToSpeechPlayer.h"
#include "D2dPresenter.h"
#include "WinFramePresenter.h"
#include "WinTextureLoader.h"
#include "WorldJsonLoader.h"

/* -------------------------------------------------------------------------- */

#define CELL_SIZE    512
#define VISUAL_DEGREE 60 

#define KEYBSTEP  10
#define KEYBALPHA  4

// Progressive player turn (Left/Right arrows): the turn rate ramps from a base value up
// to a capped maximum the longer the key is held, then resets on release or direction
// change. Rates are expressed in degrees per second (converted to the engine's angle
// units via Player::deg360()) so the turning feel is independent of the frame rate.
//
// These are the built-in fallbacks; a world can override them via its "playerTurn" JSON
// object (editable in NuRcade.Editor), read into WorldMap by WorldJsonLoader.
static constexpr double kTurnBaseDegPerSec = 90.0;    // initial rate for precise small turns
static constexpr double kTurnMaxDegPerSec = 300.0;    // cap reached after holding the key
static constexpr double kTurnAccelDegPerSec2 = 360.0; // ramp-up acceleration (~0.6s to cap)
static constexpr double kPlayerBaseViewCenter = 0.5;
static constexpr double kPlayerPropLiftViewCenter = 0.64;
static constexpr double kPlayerPropLiftEase = 0.28;
static constexpr double kPlayerPropStandRadiusCells = 0.58;

#define RENDER_X_RES 1024
#define RENDER_Y_RES 1024
#define HUD_PANEL_X_RES 384

// Default bottom-panel height (teletype event log). The actual reserved height is computed
// at runtime from the world's messageLog config (0 when the log is disabled); this value
// sizes the default window and matches the height of a full 4-line log.
#define HUD_PANEL_Y_RES 96
#define EVENT_LOG_LINE_HEIGHT 20
#define X_RES (RENDER_X_RES + HUD_PANEL_X_RES)
#define Y_RES (RENDER_Y_RES + HUD_PANEL_Y_RES)

#define PROJ_X_RES 1024
#define PROJ_Y_RES 1024

#define SCALE 500000
#define SPEED_FACTOR 2
#define RUNNING_SPEED_FACTOR 4

#define CAMERA_CEL_COL_POS 4
#define CAMERA_CEL_ROW_POS 4

#define TEST_SPRITE_TEXTURE_BASE 0x100
#define TEST_SPRITE_VIEW_COUNT 8



/* -------------------------------------------------------------------------- */

// Global Variables:
HINSTANCE g_hInstance;                // current instance
TCHAR g_szAppTitle[] = "nuRCADE Player";
TCHAR g_szAppWinClass[] = "NURCADE";
HWND g_hWnd;


/* -------------------------------------------------------------------------- */

// Foward declarations of functions included in this code module:
static ATOM WRCstRegisterClass(HINSTANCE hInstance);
LRESULT CALLBACK  WndProc(HWND, UINT, WPARAM, LPARAM);
LRESULT CALLBACK  About(HWND, UINT, WPARAM, LPARAM);

static void MovePlayer();
static void Render3DEnvironment();
static bool LoadProjectIntoEngine(const std::string& projectPath);
static bool LoadProjectOrWorldIntoEngine(const std::string& path);
static bool LoadDefaultWorldIntoEngine();
static bool SwitchToActiveLayer(
	const SceneLoader::PlayerStart* targetPlayerStart,
	bool preservePlayerStats);
static bool OpenProjectFromMenu(HWND hWnd);
static void UpdateActors();
static void UpdateLayerTransition(double deltaSeconds);
static void hideElevatorSelectionPanel() noexcept;
static void ToggleBackgroundMusic();
static void ToggleSoundEffects();
static void TogglePlayerImmortal();
static void GivePlayerAllKeys();
static void GivePlayerAllWeapons();
static void RefillPlayerAmmo();
static void RefillPlayerEnergy();
static void AdjustBackgroundMusicVolume(int deltaPercent);
static void ResetBackgroundMusicVolume();
static void SetProjectionWindowScale(double scale, bool fitToScreen);

static bool g_FullScreenModeActive = false;
static BOOL g_bActive = FALSE;   // Is application active?
static Cell g_current_cell_of_player = 0;

std::unique_ptr<WorldMap> theWorldMap;
std::unique_ptr<RaycastEngine> the3DEngine;
ActorSystem g_actorSystem;
std::vector<SpriteActor> g_spriteActors;
ULONGLONG g_lastActorUpdateMs = 0;
bool g_playerMovingThisFrame = false;
bool g_weaponFireWasPressed = false;
bool g_weaponReloadWasPressed = false;
bool g_weaponAutoReloadPending = false;

static constexpr size_t kInvalidSpriteIndex =
static_cast<size_t>(-1);

struct RuntimePlayerWeapon {
	std::string file;
	ViewWeapon weapon;
	bool unlocked = true;
};

std::vector<RuntimePlayerWeapon> g_playerWeapons;
size_t g_activePlayerWeaponIndex = 0;
bool g_weaponSwitchWasPressed[9] = {};

struct RuntimeSpriteInfo {
	size_t spriteIndex = 0;
	std::string name;
	std::string spriteSet;
	std::string layerId;
	std::string persistenceKey;
	std::string keyId;
	std::string pickupWeapon;
	double pickupHealth = 0.0;
	bool unlocksMap = false;
	bool savePoint = false;
	bool explosive = false;
	double explosiveHitPoints = 45.0;
	double explosiveHealth = 45.0;
	double explosionRadiusCells = 0.0;
	double explosionDamage = 0.0;
	double explosionScaleCells = 1.5;
	std::string explosionSpriteSet;
	std::string destroyedSpriteSet;
	double destroyedScaleCells = 0.55;
	std::string damageResponseType;
	std::string damageEffectAnimation;
	std::string damageEffectSound;
	size_t explosionSpriteIndex = kInvalidSpriteIndex;
	size_t destroyedSpriteIndex = kInvalidSpriteIndex;
	bool explosionActive = false;
	double explosionElapsedSeconds = 0.0;
	double explosionDurationSeconds = 0.0;
	bool consumed = false;
	bool blocksPlayer = false;
};

std::vector<RuntimeSpriteInfo> g_runtimeSpriteInfos;
std::map<std::string, bool> g_runtimeSpriteConsumedByKey;
std::map<std::string, bool> g_runtimeSpriteExplodedByKey;
std::map<std::string, double> g_runtimeSpriteExplosiveHealthByKey;
std::vector<std::string> g_playerKeyIds;
std::map<std::string, std::shared_ptr<Texture>> g_keyHudTextures;

struct RuntimeActorState {
	double x = 0.0;
	double y = 0.0;
	double facingRadians = 0.0;
	double health = 0.0;
	bool dead = false;
	bool visible = true;
	bool deathAnimationStarted = false;
};

std::map<std::string, RuntimeActorState> g_runtimeActorStateByKey;

struct PlayerCombatStats {
	double maxHealth = 100.0;
	double health = 100.0;
};

PlayerCombatStats g_playerCombatStats;

struct PlayerWeaponCheckpoint {
	std::string file;
	bool unlocked = true;
	int ammoInMagazine = 0;
	int reserveAmmo = 0;
	bool usesAmmo = false;
};

struct GameCheckpoint {
	bool valid = false;
	std::string layerId;
	double playerX = 0.0;
	double playerY = 0.0;
	int playerAlpha = 0;
	int playerSlope = 0;
	double playerCenterProj = 0.5;
	PlayerCombatStats combatStats;
	std::vector<std::string> keyIds;
	bool minimapUnlocked = false;
	bool minimapActorsUnlocked = false;
	std::map<std::string, bool> runtimeSpriteConsumedByKey;
	std::map<std::string, bool> runtimeSpriteExplodedByKey;
	std::map<std::string, double> runtimeSpriteExplosiveHealthByKey;
	std::map<std::string, RuntimeActorState> runtimeActorStateByKey;
	std::vector<PlayerWeaponCheckpoint> weapons;
	std::string activeWeaponFile;
};

enum class PlayerLifeState {
	Alive,
	Dying,
	Respawning
};

struct GameCompletionStats {
	int totalEnemies = 0;
	int killedEnemies = 0;
	int totalKeys = 0;
	int collectedKeys = 0;
	int totalItems = 0;
	int acquiredItems = 0;
	int totalDestructibleProps = 0;
	int destroyedProps = 0;
};

struct MissionObjectiveState {
	std::vector<std::string> enemyPersistenceKeys;
	std::vector<std::string> keyPersistenceKeys;
	std::vector<std::string> itemPersistenceKeys;
	std::vector<std::string> destructiblePropPersistenceKeys;
};

struct LeaderboardEntry {
	std::string name;
	int score = 0;
	double completionSeconds = 0.0;
};

struct CompletionSummaryState {
	bool active = false;
	bool countingComplete = false;
	bool enteringName = false;
	bool awaitingRestart = false;
	int counterStage = 0;
	int displayedEnemies = 0;
	int displayedItems = 0;
	int displayedDestroyedProps = 0;
	int displayedScore = 0;
	double tickSoundCooldown = 0.0;
	GameCompletionStats stats;
	double completionSeconds = 0.0;
	int itemPoints = 0;
	int enemyPoints = 0;
	int destructionPenalty = 0;
	int timeBonus = 0;
	int totalScore = 0;
	std::string playerName;
	std::vector<LeaderboardEntry> leaderboard;
};

MissionObjectiveState g_missionObjectives;
GameCheckpoint g_autoCheckpoint;
PlayerLifeState g_playerLifeState = PlayerLifeState::Alive;
double g_playerDeathElapsedSeconds = 0.0;
bool g_playerDeathMessageShown = false;
bool g_gameCompleted = false;
bool g_gameCompletedMessageShown = false;
double g_missionElapsedSeconds = 0.0;
CompletionSummaryState g_completionSummary;

struct LayerTransition {
	std::string fromLayer;
	std::string toLayer;
	std::string requiredKey;
	std::string triggerBlockId;
	int triggerRow = -1;
	int triggerColumn = -1;
	bool hasTriggerCell = false;
	double waitSeconds = 1.5;
	SceneLoader::PlayerStart targetPlayerStart;
	bool hasTargetPlayerStart = false;
};

struct PendingLayerTransition {
	bool active = false;
	std::string targetLayer;
	double elapsedSeconds = 0.0;
	double waitSeconds = 1.5;
	SceneLoader::PlayerStart targetPlayerStart;
	bool hasTargetPlayerStart = false;
	int triggerRow = -1;
	int triggerColumn = -1;
};

struct ElevatorSelectionPanel {
	bool visible = false;
	int row = -1;
	int column = -1;
	uint8_t blockId = 0;
	std::vector<size_t> transitionIndices;
	size_t selectedIndex = 0;
};

struct ElevatorShake {
	bool active = false;
	double elapsedSeconds = 0.0;
	double totalSeconds = 0.0;
	int baseSlope = 0;
	double baseCenterProj = 0.5;
	bool hasBaseline = false;
};

struct GameGoal {
	std::string layerId;
	std::string requiredKey;
	int row = -1;
	int column = -1;
	bool configured = false;
};

struct SavePointPanel {
	bool visible = false;
	std::string persistenceKey;
	std::string stateSignature;
};

std::string g_currentProjectPath;
std::string g_currentWorldPath;
std::string g_currentWorldDir;
std::string g_currentProjectDir;
std::string g_activeLayerId;
std::vector<LayerTransition> g_layerTransitions;
GameGoal g_gameGoal;
PendingLayerTransition g_pendingLayerTransition;
ElevatorSelectionPanel g_elevatorPanel;
bool g_layerTransitionArmed = true;
ElevatorShake g_elevatorShake;
std::map<std::string, std::string> g_layerDisplayNames;
std::vector<std::string> g_developerLayerMenuIds;
HMENU g_developerLayerMenu = nullptr;
BackgroundMusicPlayer g_backgroundMusicPlayer;
TextToSpeechPlayer g_textToSpeechPlayer;
std::string g_backgroundMusicPath;
bool g_backgroundMusicLoop = true;
bool g_backgroundMusicCurrentLoop = true;
bool g_backgroundMusicEnabled = true;
bool g_backgroundMusicWarningShown = false;
int g_backgroundMusicVolumePercent = 80;
int g_backgroundMusicInitialVolumePercent = 80;
bool g_soundEffectWarningShown = false;
bool g_textToSpeechWarningShown = false;
bool g_soundEffectsEnabled = true;
bool g_eventSpeechEnabled = false;
bool g_importantEventSpeechEnabled = true;
bool g_playerImmortal = false;
bool g_developerMode = false;
bool g_minimapUnlocked = false;
bool g_minimapActorsUnlocked = false;
double g_projectionWindowScale = 1.0;
bool g_projectionWindowFitToScreen = false;
double g_damageFlashSeconds = 0.0;
constexpr int kInitialPlayerLives = 3;
int g_playerLivesRemaining = kInitialPlayerLives;
bool g_gameOver = false;
std::string g_activeSavePointPromptKey;
SavePointPanel g_savePointPanel;
std::map<std::string, std::string> g_savedStateSignatureByPoint;
bool g_savePointEnterWasPressed = false;
bool g_savePointEscapeWasPressed = false;
ULONGLONG g_lastDoorEffectTimeMs = 0;
int g_lastDoorEffectRow = -1;
int g_lastDoorEffectColumn = -1;
bool g_elevatorPanelUpWasPressed = false;
bool g_elevatorPanelDownWasPressed = false;
bool g_elevatorPanelEnterWasPressed = false;
bool g_elevatorPanelEscapeWasPressed = false;

struct PlayerStatusHudFrame {
	double maxHealthPercent = 1.0;
	std::shared_ptr<Texture> texture;
};

std::vector<PlayerStatusHudFrame> g_playerStatusHudFrames;

// Teletype event log shown in the bottom panel: a short rolling history of gameplay
// events (pickups, keys, health, ammo, ...). Newest messages appear at the bottom.
struct EventLogLine {
	std::string text;
	ULONGLONG timeMs = 0;
};

std::deque<EventLogLine> g_eventLog;

// Per-frame performance stats (exponentially smoothed), shown as an on-screen
// FPS / phase breakdown in the HUD. Written each frame by the main loop and
// Render3DEnvironment, read by drawRuntimeHud. Toggle with F6.
struct FrameStats {
	double fps = 0.0;
	double frameMs = 0.0;    // full loop iteration (incl. message pump)
	double updateMs = 0.0;   // MovePlayer + UpdateActors
	double renderMs = 0.0;   // RaycastEngine::renderToFrameBuffer
	double presentMs = 0.0;  // presentFrameBuffer (GDI upscale blit)
	double hudMs = 0.0;      // drawRuntimeHud (this overlay; one frame behind)
};

FrameStats g_frameStats;
bool g_showPerfHud = true;

// Hardware-accelerated presenter (Direct2D). When ready, it replaces the
// DirectDraw + GDI StretchDIBits present path; otherwise the legacy path is
// used. Only enabled in windowed mode (fullscreen uses DirectDraw exclusive).
D2dPresenter g_presenter;

inline void emaUpdate(double& accumulator, double sample) noexcept {
	constexpr double alpha = 0.1; // smoothing factor
	accumulator = accumulator <= 0.0
		? sample
		: accumulator + alpha * (sample - accumulator);
}

namespace {
	constexpr double kPi = 3.14159265358979323846;
	constexpr const char* kKeyPickupSoundPath = "effects/pickups/bling1.mp3";
	constexpr const char* kAmmoPickupSoundPath = "effects/pickups/beep4.mp3";
	constexpr const char* kMedikitPickupSoundPath = "effects/pickups/beep5.mp3";
	constexpr const char* kComputerPickupSoundPath = "effects/pickups/beep6.mp3";
	constexpr const char* kExplosionSoundPath = "effects/explosions/cannon1.mp3";
	constexpr const char* kEnemyRangedAttackSoundPath =
		"weapons/submachine_gun/sounds/machine_gun_burst_dry_close.wav";
	constexpr const char* kEnemyMeleeAttackSoundPath =
		"effects/enemies/punch1.mp3";
	constexpr double kWeaponNoiseAlertSeconds = 6.0;
	constexpr double kWeaponNoiseRadiusMultiplier = 2.75;
	constexpr double kWeaponNoiseExtraRadiusCells = 6.0;
	constexpr double kWeaponNoiseMinimumRadiusCells = 8.0;
	constexpr double kWeaponNoiseMaximumRadiusCells = 24.0;
	constexpr double kPlayerEnergyDrainPerSecond = 0.35;
	constexpr double kPlayerDeathFadeSeconds = 2.0;
	constexpr double kPlayerDeathHoldSeconds = 1.25;

	constexpr const char* kAboutText =
		"nuRCADE Player by antonino.calderone@gmail.com\r\n"
		"Copyright (C) 2005 - 2018 Antonino Calderone\r\n"
		"Licensed under the MIT License. See COPYING in the project root.\r\n"
		"\r\n"
		"Third-party demo audio resources:\r\n"
		"- bling1.mp3, key pickup: JustinBW @ FreeSound (2009), "
		"CC-BY-3.0, https://freesound.org/s/80921/\r\n"
		"- beep4.mp3, ammo pickup: KevanGC @ SoundBible (2010), "
		"CC0, http://soundbible.com/1645-Pling.html\r\n"
		"- beep5.mp3, medikit pickup: Soundwarf @ FreeSound (2017), "
		"CC0, https://freesound.org/s/387532/\r\n"
		"- beep6.mp3, computer/map pickup: kickhat @ FreeSound (2015), "
		"CC0, https://freesound.org/s/264446/\r\n"
		"- cannon2.mp3, super shotgun fire: Isaac200000 @ FreeSound (2013), "
		"CC0, https://freesound.org/s/184650/\r\n"
		"- cannon1.mp3, item explosion: nps.gov @ SoundBible (2009), "
		"CC0, http://soundbible.com/909-Cannon.html\r\n"
		"- punch1.mp3, enemy melee hit: Vladimir @ SoundBible (2011), "
		"CC0, http://soundbible.com/1952-Punch-Or-Whack.html\r\n"
		"\r\n"
		"The copied sound files keep their source license text beside them. "
		"See res/worlds/demo_embedded/ASSET_ATTRIBUTIONS.md for the same list.";

	std::string formatDouble(double value, int decimals);
	bool tryParseBlockId(const std::string& text, uint8_t& blockId) noexcept;

	int clampInt(int value, int minValue, int maxValue) noexcept
	{
		return (std::min)(maxValue, (std::max)(minValue, value));
	}

	void replaceAllInPlace(std::string& text, const std::string& from, const std::string& to)
	{
		if (from.empty()) {
			return;
		}

		size_t pos = 0;
		while ((pos = text.find(from, pos)) != std::string::npos) {
			text.replace(pos, from.size(), to);
			pos += to.size();
		}
	}

	std::string prettifyEventName(std::string name)
	{
		std::replace(name.begin(), name.end(), '_', ' ');
		return name;
	}

	// Fills {name}/{amount} placeholders in a world-configured message template.
	std::string formatEventMessage(
		std::string templateText,
		const std::string& name,
		const std::string& amount)
	{
		replaceAllInPlace(templateText, "{name}", name);
		replaceAllInPlace(templateText, "{amount}", amount);
		return templateText;
	}

	void pushEventMessage(const std::string& text, bool important = false)
	{
		if (text.empty()) {
			return;
		}

		g_eventLog.push_back(EventLogLine{ text, GetTickCount64() });
		while (g_eventLog.size() > 8) {
			g_eventLog.pop_front();
		}

		if (g_eventSpeechEnabled || (important && g_importantEventSpeechEnabled)) {
			std::string error;
			if (!g_textToSpeechPlayer.speak(text, &error)
				&& !g_textToSpeechWarningShown) {
				MessageBox(
					g_hWnd,
					("Could not read event message with text-to-speech:\n" + error).c_str(),
					g_szAppTitle,
					MB_OK | MB_ICONWARNING);
				g_textToSpeechWarningShown = true;
			}
		}
	}

	struct GdiObjectDeleter {
		void operator()(HGDIOBJ object) const noexcept
		{
			if (object) {
				DeleteObject(object);
			}
		}
	};

	struct DcDeleter {
		void operator()(HDC hdc) const noexcept
		{
			if (hdc) {
				DeleteDC(hdc);
			}
		}
	};

	using BitmapHandle =
		std::unique_ptr<std::remove_pointer<HBITMAP>::type, GdiObjectDeleter>;
	using BrushHandle =
		std::unique_ptr<std::remove_pointer<HBRUSH>::type, GdiObjectDeleter>;
	using PenHandle =
		std::unique_ptr<std::remove_pointer<HPEN>::type, GdiObjectDeleter>;
	using FontHandle =
		std::unique_ptr<std::remove_pointer<HFONT>::type, GdiObjectDeleter>;
	using MemoryDcHandle =
		std::unique_ptr<std::remove_pointer<HDC>::type, DcDeleter>;

	class SelectObjectScope {
	public:
		SelectObjectScope(HDC hdc, HGDIOBJ object) noexcept
			: m_hdc(hdc)
			, m_previous(object != nullptr ? SelectObject(hdc, object) : nullptr)
		{}

		~SelectObjectScope()
		{
			if (m_hdc && m_previous) {
				SelectObject(m_hdc, m_previous);
			}
		}

	private:
		HDC m_hdc = nullptr;
		HGDIOBJ m_previous = nullptr;
	};

	std::string trimQuotesAndWhitespace(std::string text)
	{
		while (!text.empty() && (text.front() == ' ' || text.front() == '\t' || text.front() == '"')) {
			text.erase(text.begin());
		}

		while (!text.empty() && (text.back() == ' ' || text.back() == '\t' || text.back() == '"')) {
			text.pop_back();
		}

		return text;
	}

	bool endsWithIgnoreCase(const std::string& value, const std::string& suffix)
	{
		if (value.size() < suffix.size()) {
			return false;
		}

		return std::equal(suffix.rbegin(), suffix.rend(), value.rbegin(),
			[](char lhs, char rhs) {
				return std::tolower(static_cast<unsigned char>(lhs))
					== std::tolower(static_cast<unsigned char>(rhs));
			});
	}

	std::string lowerCopy(std::string value)
	{
		std::transform(value.begin(), value.end(), value.begin(),
			[](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
		return value;
	}

	bool containsIgnoreCase(const std::string& value, const std::string& needle)
	{
		return lowerCopy(value).find(lowerCopy(needle)) != std::string::npos;
	}

	bool equalsIgnoreCase(const std::string& value, const std::string& other)
	{
		return lowerCopy(value) == lowerCopy(other);
	}

	std::string normalizeResourcePathForCompare(std::string value)
	{
		std::replace(value.begin(), value.end(), '\\', '/');
		value = lowerCopy(value);
		while (value.rfind("./", 0) == 0) {
			value.erase(0, 2);
		}

		return value;
	}

	bool looksLikeWorldJson(const std::string& path)
	{
		if (endsWithIgnoreCase(path, ".world.json")) {
			return true;
		}

		std::ifstream input(path);
		if (!input.is_open()) {
			return false;
		}

		std::string text;
		text.resize(4096);
		input.read(&text[0], static_cast<std::streamsize>(text.size()));
		text.resize(static_cast<size_t>(input.gcount()));

		return text.find("\"format\"") != std::string::npos
			&& text.find("\"nurcade.world\"") != std::string::npos;
	}

	std::vector<std::string> splitCommandLine(LPSTR lpCmdLine)
	{
		std::vector<std::string> args;
		if (!lpCmdLine) {
			return args;
		}

		const std::string text(lpCmdLine);
		std::string current;
		bool inQuotes = false;
		for (char ch : text) {
			if (ch == '"') {
				inQuotes = !inQuotes;
				continue;
			}

			if (!inQuotes && (ch == ' ' || ch == '\t')) {
				if (!current.empty()) {
					args.push_back(current);
					current.clear();
				}
				continue;
			}

			current.push_back(ch);
		}

		if (!current.empty()) {
			args.push_back(current);
		}

		return args;
	}

	struct CommandLineOptions {
		std::string projectPath;
		bool backgroundMusicEnabled = true;
		bool soundEffectsEnabled = true;
		bool playerImmortal = false;
		bool testTextToSpeech = false;
		bool developerMode = false;
	};

	CommandLineOptions parseCommandLineOptions(LPSTR lpCmdLine)
	{
		CommandLineOptions options;
		for (const auto& arg : splitCommandLine(lpCmdLine)) {
			const auto trimmed = trimQuotesAndWhitespace(arg);
			if (trimmed.empty()) {
				continue;
			}

			if (trimmed == "--no-music" || trimmed == "/nomusic") {
				options.backgroundMusicEnabled = false;
				continue;
			}

			if (trimmed == "--no-effects" || trimmed == "/noeffects") {
				options.soundEffectsEnabled = false;
				continue;
			}

			if (trimmed == "--immortal" || trimmed == "/immortal"
				|| trimmed == "--god" || trimmed == "/god") {
				options.playerImmortal = true;
				continue;
			}

			if (trimmed == "--tts-test") {
				options.testTextToSpeech = true;
				continue;
			}

			if (trimmed == "--dev") {
				options.developerMode = true;
				continue;
			}

			if (endsWithIgnoreCase(trimmed, ".json")) {
				options.projectPath = trimmed;
			}
		}

		return options;
	}

	std::string directoryOf(const std::string& path)
	{
		const auto slash = path.find_last_of("/\\");
		if (slash == std::string::npos) {
			return {};
		}

		return path.substr(0, slash + 1);
	}

	std::string joinPath(const std::string& base, const std::string& relative)
	{
		if (relative.size() > 1 && relative[1] == ':') {
			return relative;
		}

		if (!relative.empty() && (relative[0] == '/' || relative[0] == '\\')) {
			return relative;
		}

		if (base.empty()) {
			return relative;
		}

		return base + relative;
	}

	std::string currentWorldAssetBaseDir()
	{
		return g_currentWorldDir.empty()
			? g_currentProjectDir
			: g_currentWorldDir;
	}

	bool fileExists(const std::string& path) noexcept
	{
		return !path.empty()
			&& GetFileAttributesA(path.c_str()) != INVALID_FILE_ATTRIBUTES;
	}

	std::string firstExistingPath(
		const std::string& baseDir,
		const std::vector<std::string>& relativePaths)
	{
		for (const auto& relativePath : relativePaths) {
			auto path = joinPath(baseDir, relativePath);
			if (fileExists(path)) {
				return path;
			}
		}

		return {};
	}

	int clampPercent(int value) noexcept
	{
		if (value < 0) {
			return 0;
		}

		if (value > 100) {
			return 100;
		}

		return value;
	}

	double clampProjectionWindowScale(double scale) noexcept
	{
		if (scale < 0.5) {
			return 0.5;
		}

		if (scale > 3.0) {
			return 3.0;
		}

		return scale;
	}

	int projectionWindowScaleMenuId(double scale) noexcept
	{
		struct ScaleMenuItem {
			double scale = 1.0;
			int id = 0;
		};

		static const ScaleMenuItem items[] = {
			{ 0.75, ID_VIEW_PROJECTION_75 },
			{ 0.90, ID_VIEW_PROJECTION_90 },
			{ 1.00, ID_VIEW_PROJECTION_100 },
			{ 1.25, ID_VIEW_PROJECTION_125 },
			{ 1.50, ID_VIEW_PROJECTION_150 },
			{ 2.00, ID_VIEW_PROJECTION_200 },
		};

		for (const auto& item : items) {
			if (std::abs(scale - item.scale) < 0.005) {
				return item.id;
			}
		}

		return 0;
	}

	RECT adjustedWindowRectForClientSize(int clientWidth, int clientHeight)
	{
		RECT windowRect{ 0, 0, clientWidth, clientHeight };
		const auto style = g_hWnd
			? static_cast<DWORD>(GetWindowLongPtr(g_hWnd, GWL_STYLE))
			: static_cast<DWORD>(WS_POPUPWINDOW | WS_CAPTION | WS_BORDER);
		const auto exStyle = g_hWnd
			? static_cast<DWORD>(GetWindowLongPtr(g_hWnd, GWL_EXSTYLE))
			: static_cast<DWORD>(WS_EX_TOPMOST);
		const auto hasMenu = g_hWnd && GetMenu(g_hWnd) != nullptr;
		AdjustWindowRectEx(&windowRect, style, hasMenu, exStyle);
		return windowRect;
	}

	bool monitorWorkAreaForWindow(RECT& workArea) noexcept
	{
		if (!g_hWnd) {
			return false;
		}

		MONITORINFO monitorInfo{};
		monitorInfo.cbSize = sizeof(monitorInfo);
		const auto monitor = MonitorFromWindow(g_hWnd, MONITOR_DEFAULTTONEAREST);
		if (!monitor || !GetMonitorInfo(monitor, &monitorInfo)) {
			return false;
		}

		workArea = monitorInfo.rcWork;
		return true;
	}

	bool outerWindowFitsWorkArea(double scale, const RECT& workArea)
	{
		const auto clientWidth =
			static_cast<int>(std::round(RENDER_X_RES * scale)) + HUD_PANEL_X_RES;
		const auto clientHeight =
			static_cast<int>(std::round(RENDER_Y_RES * scale)) + HUD_PANEL_Y_RES;
		auto windowRect = adjustedWindowRectForClientSize(clientWidth, clientHeight);
		const auto windowWidth = static_cast<int>(windowRect.right - windowRect.left);
		const auto windowHeight = static_cast<int>(windowRect.bottom - windowRect.top);
		return windowWidth <= workArea.right - workArea.left
			&& windowHeight <= workArea.bottom - workArea.top;
	}

	double projectionWindowFitScaleToScreen()
	{
		RECT workArea{};
		if (!monitorWorkAreaForWindow(workArea)) {
			return 1.0;
		}

		double low = 0.5;
		double high = 3.0;
		for (int iteration = 0; iteration < 32; ++iteration) {
			const auto mid = (low + high) * 0.5;
			if (outerWindowFitsWorkArea(mid, workArea)) {
				low = mid;
			}
			else {
				high = mid;
			}
		}

		return clampProjectionWindowScale(low);
	}

	void resizeWindowForProjectionScale(double scale)
	{
		if (!g_hWnd || g_FullScreenModeActive) {
			return;
		}

		RECT workArea{};
		monitorWorkAreaForWindow(workArea);

		const auto clientWidth =
			static_cast<int>(std::round(RENDER_X_RES * scale)) + HUD_PANEL_X_RES;
		const auto clientHeight =
			static_cast<int>(std::round(RENDER_Y_RES * scale)) + HUD_PANEL_Y_RES;
		auto windowRect = adjustedWindowRectForClientSize(clientWidth, clientHeight);
		const auto windowWidth = static_cast<int>(windowRect.right - windowRect.left);
		const auto windowHeight = static_cast<int>(windowRect.bottom - windowRect.top);

		auto windowX = 0;
		auto windowY = 0;
		if (workArea.right > workArea.left && workArea.bottom > workArea.top) {
			const auto workWidth = static_cast<int>(workArea.right - workArea.left);
			const auto workHeight = static_cast<int>(workArea.bottom - workArea.top);
			windowX = static_cast<int>(workArea.left)
				+ (std::max)(0, (workWidth - windowWidth) / 2);
			windowY = static_cast<int>(workArea.top)
				+ (std::max)(0, (workHeight - windowHeight) / 2);
		}

		SetWindowPos(
			g_hWnd,
			nullptr,
			windowX,
			windowY,
			windowWidth,
			windowHeight,
			SWP_NOZORDER | SWP_NOACTIVATE);
		InvalidateRect(g_hWnd, nullptr, FALSE);
	}

	void updateProjectionWindowMenu(HMENU menu)
	{
		static const int scaleIds[] = {
			ID_VIEW_PROJECTION_75,
			ID_VIEW_PROJECTION_90,
			ID_VIEW_PROJECTION_100,
			ID_VIEW_PROJECTION_125,
			ID_VIEW_PROJECTION_150,
			ID_VIEW_PROJECTION_200,
			ID_VIEW_PROJECTION_FIT_SCREEN,
		};

		for (const auto id : scaleIds) {
			CheckMenuItem(menu, id, MF_BYCOMMAND | MF_UNCHECKED);
		}

		if (g_projectionWindowFitToScreen) {
			CheckMenuItem(
				menu,
				ID_VIEW_PROJECTION_FIT_SCREEN,
				MF_BYCOMMAND | MF_CHECKED);
			return;
		}

		const auto presetId = projectionWindowScaleMenuId(g_projectionWindowScale);
		if (presetId != 0) {
			CheckMenuItem(menu, presetId, MF_BYCOMMAND | MF_CHECKED);
		}
	}

	void updateBackgroundMusicMenu()
	{
		if (!g_hWnd) {
			return;
		}

		const auto menu = GetMenu(g_hWnd);
		if (!menu) {
			return;
		}

		CheckMenuItem(
			menu,
			ID_AUDIO_BACKGROUND_MUSIC,
			MF_BYCOMMAND | (g_backgroundMusicEnabled ? MF_CHECKED : MF_UNCHECKED));

		CheckMenuItem(
			menu,
			ID_AUDIO_SOUND_EFFECTS,
			MF_BYCOMMAND | (g_soundEffectsEnabled ? MF_CHECKED : MF_UNCHECKED));

		CheckMenuItem(
			menu,
			ID_AUDIO_EVENT_SPEECH,
			MF_BYCOMMAND | (g_eventSpeechEnabled ? MF_CHECKED : MF_UNCHECKED));

		CheckMenuItem(
			menu,
			ID_GAME_IMMORTAL,
			MF_BYCOMMAND | (g_playerImmortal ? MF_CHECKED : MF_UNCHECKED));

		const auto volumeText =
			"Volume &up\tF11 (" + std::to_string(g_backgroundMusicVolumePercent) + "%)";
		ModifyMenuA(
			menu,
			ID_AUDIO_VOLUME_UP,
			MF_BYCOMMAND | MF_STRING,
			ID_AUDIO_VOLUME_UP,
			volumeText.c_str());

		const auto volumeMenuState = g_backgroundMusicPath.empty()
			? MF_GRAYED
			: MF_ENABLED;
		EnableMenuItem(menu, ID_AUDIO_VOLUME_DOWN, MF_BYCOMMAND | volumeMenuState);
		EnableMenuItem(menu, ID_AUDIO_VOLUME_UP, MF_BYCOMMAND | volumeMenuState);
		EnableMenuItem(menu, ID_AUDIO_VOLUME_RESET, MF_BYCOMMAND | volumeMenuState);
		updateProjectionWindowMenu(menu);
	}

	int topLevelMenuIndexContainingCommand(HMENU menu, UINT commandId) noexcept
	{
		if (!menu) {
			return -1;
		}

		const auto count = GetMenuItemCount(menu);
		for (int index = 0; index < count; ++index) {
			const auto submenu = GetSubMenu(menu, index);
			if (submenu != nullptr
				&& GetMenuState(submenu, commandId, MF_BYCOMMAND) != UINT(-1)) {
				return index;
			}
		}

		return -1;
	}

	void removeDeveloperLayerMenu(HMENU menu) noexcept
	{
		if (!menu || !g_developerLayerMenu) {
			return;
		}

		const auto count = GetMenuItemCount(menu);
		for (int index = 0; index < count; ++index) {
			if (GetSubMenu(menu, index) == g_developerLayerMenu) {
				RemoveMenu(menu, index, MF_BYPOSITION);
				break;
			}
		}

		DestroyMenu(g_developerLayerMenu);
		g_developerLayerMenu = nullptr;
		g_developerLayerMenuIds.clear();
	}

	void configureDeveloperMenus()
	{
		if (!g_hWnd) {
			return;
		}

		const auto menu = GetMenu(g_hWnd);
		if (!menu) {
			return;
		}

		removeDeveloperLayerMenu(menu);
		const auto gameMenuIndex =
			topLevelMenuIndexContainingCommand(menu, ID_GAME_IMMORTAL);
		if (!g_developerMode) {
			if (gameMenuIndex >= 0) {
				RemoveMenu(menu, gameMenuIndex, MF_BYPOSITION);
			}
			DrawMenuBar(g_hWnd);
			return;
		}

		if (!g_layerDisplayNames.empty()) {
			g_developerLayerMenu = CreatePopupMenu();
			if (g_developerLayerMenu != nullptr) {
				for (const auto& [layerId, displayName] : g_layerDisplayNames) {
					if (g_developerLayerMenuIds.size()
						>= ID_DEV_LEVEL_LAST - ID_DEV_LEVEL_FIRST + 1) {
						break;
					}

					auto label = displayName.empty()
						? std::string("Uncharted destination")
						: displayName;
					replaceAllInPlace(label, "&", "&&");
					const auto commandId = ID_DEV_LEVEL_FIRST
						+ static_cast<UINT>(g_developerLayerMenuIds.size());
					AppendMenuA(
						g_developerLayerMenu,
						MF_STRING,
						commandId,
						label.c_str());
					g_developerLayerMenuIds.push_back(layerId);
				}

				AppendMenuA(
					menu,
					MF_POPUP,
					reinterpret_cast<UINT_PTR>(g_developerLayerMenu),
					"&Level");
				for (size_t index = 0; index < g_developerLayerMenuIds.size(); ++index) {
					if (g_developerLayerMenuIds[index] == g_activeLayerId) {
						CheckMenuRadioItem(
							g_developerLayerMenu,
							ID_DEV_LEVEL_FIRST,
							ID_DEV_LEVEL_LAST,
							ID_DEV_LEVEL_FIRST + static_cast<UINT>(index),
							MF_BYCOMMAND);
						break;
					}
				}
			}
		}

		DrawMenuBar(g_hWnd);
	}

	void applyBackgroundMusicState(bool restart = false)
	{
		updateBackgroundMusicMenu();

		if (!g_backgroundMusicEnabled || g_backgroundMusicPath.empty()) {
			g_backgroundMusicPlayer.stop();
			return;
		}

		if (g_backgroundMusicPlayer.isOpen()
			&& g_backgroundMusicPlayer.currentPath() == g_backgroundMusicPath
			&& g_backgroundMusicCurrentLoop == g_backgroundMusicLoop
			&& !restart) {
			std::string error;
			if (!g_backgroundMusicPlayer.setVolumePercent(
				g_backgroundMusicVolumePercent,
				&error)
				&& !g_backgroundMusicWarningShown) {
				MessageBox(
					g_hWnd,
					("Could not set background music volume:\n" + error).c_str(),
					g_szAppTitle,
					MB_OK | MB_ICONWARNING);
				g_backgroundMusicWarningShown = true;
			}
			return;
		}

		std::string error;
		if (!g_backgroundMusicPlayer.play(
			g_backgroundMusicPath,
			g_backgroundMusicLoop,
			g_backgroundMusicVolumePercent,
			&error)) {
			g_backgroundMusicPlayer.stop();
			if (!g_backgroundMusicWarningShown) {
				MessageBox(
					g_hWnd,
					("Could not play background music:\n" + error).c_str(),
					g_szAppTitle,
					MB_OK | MB_ICONWARNING);
				g_backgroundMusicWarningShown = true;
			}
			return;
		}

		g_backgroundMusicCurrentLoop = g_backgroundMusicLoop;
	}

	void setBackgroundMusicFromScene(
		const SceneLoader::Scene* scene,
		const std::string& projectDir)
	{
		g_backgroundMusicPath.clear();
		g_backgroundMusicLoop = true;
		g_backgroundMusicVolumePercent = g_backgroundMusicInitialVolumePercent;
		g_backgroundMusicWarningShown = false;

		if (scene != nullptr
			&& scene->backgroundMusic.enabled
			&& !scene->backgroundMusic.file.empty()) {
			g_backgroundMusicPath = joinPath(projectDir, scene->backgroundMusic.file);
			g_backgroundMusicLoop = scene->backgroundMusic.loop;
			g_backgroundMusicVolumePercent =
				clampPercent(scene->backgroundMusic.volumePercent);
			g_backgroundMusicInitialVolumePercent = g_backgroundMusicVolumePercent;
		}

		applyBackgroundMusicState(true);
	}

	void playDoorOpeningEffectAt(int row, int column)
	{
		if (!g_soundEffectsEnabled) {
			return;
		}

		if (!theWorldMap || row < 0 || column < 0) {
			return;
		}

		const auto* block = theWorldMap->blockAtCell(row, column);
		if (block == nullptr || block->door.openSound.empty()) {
			return;
		}

		const auto now = GetTickCount64();
		if (g_lastDoorEffectRow == row
			&& g_lastDoorEffectColumn == column
			&& now - g_lastDoorEffectTimeMs < 750) {
			return;
		}

		g_lastDoorEffectRow = row;
		g_lastDoorEffectColumn = column;
		g_lastDoorEffectTimeMs = now;

		const auto baseDir = currentWorldAssetBaseDir();
		std::string error;
		const auto soundPath = joinPath(baseDir, block->door.openSound);
		if (!SoundEffectPlayer::playOnce(
			soundPath,
			block->door.openSoundVolumePercent,
			&error)
			&& !g_soundEffectWarningShown) {
			MessageBox(
				g_hWnd,
				("Could not play sound effect:\n" + error).c_str(),
				g_szAppTitle,
				MB_OK | MB_ICONWARNING);
			g_soundEffectWarningShown = true;
		}
	}

	void playWorldSoundEffect(const char* relativePath, int volumePercent = 100)
	{
		if (!g_soundEffectsEnabled || relativePath == nullptr || relativePath[0] == '\0') {
			return;
		}

		const auto baseDir = currentWorldAssetBaseDir();
		if (baseDir.empty()) {
			return;
		}

		const auto soundPath = joinPath(baseDir, relativePath);
		if (GetFileAttributesA(soundPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
			return;
		}

		std::string error;
		if (!SoundEffectPlayer::playOnce(soundPath, volumePercent, &error)
			&& !g_soundEffectWarningShown) {
			MessageBox(
				g_hWnd,
				("Could not play sound effect:\n" + error).c_str(),
				g_szAppTitle,
				MB_OK | MB_ICONWARNING);
			g_soundEffectWarningShown = true;
		}
	}

	void playDoorOpeningEffects(const std::vector<WorldMap::DoorEvent>& events)
	{
		for (const auto& event : events) {
			if (event.type != WorldMap::DoorEvent::Type::OpeningStarted) {
				continue;
			}

			playDoorOpeningEffectAt(event.row, event.column);
		}
	}

	void loadPlayerStatusHudFrames(const std::string& baseDir)
	{
		g_playerStatusHudFrames.clear();

		struct FrameSource {
			double maxHealthPercent = 1.0;
			const char* name = nullptr;
		};

		static const FrameSource frames[] = {
			{ 1.00, "status_100" },
			{ 0.80, "status_80" },
			{ 0.60, "status_60" },
			{ 0.40, "status_40" },
			{ 0.20, "status_20" },
			{ 0.05, "status_5" },
			{ 0.00, "status_0" }
		};

		for (const auto& frame : frames) {
			const auto path = firstExistingPath(
				baseDir,
				{
					std::string("hud/player_status/") + frame.name + ".bmp",
					std::string("hud/player_status/") + frame.name + ".png"
				});
			auto texture = loadTextureFromFile(path, 0, 0);
			if (!texture) {
				g_playerStatusHudFrames.clear();
				return;
			}

			g_playerStatusHudFrames.push_back({ frame.maxHealthPercent, texture });
		}
	}

	const Texture* playerStatusHudFrameForHealth(double healthPercent) noexcept
	{
		if (g_playerStatusHudFrames.empty()) {
			return nullptr;
		}

		static const double thresholds[] = { 0.90, 0.70, 0.50, 0.30, 0.10, 0.0 };
		size_t index = 0;
		for (; index < sizeof(thresholds) / sizeof(thresholds[0]); ++index) {
			if (healthPercent > thresholds[index]) {
				break;
			}
		}

		if (index >= g_playerStatusHudFrames.size()) {
			index = g_playerStatusHudFrames.size() - 1;
		}

		return g_playerStatusHudFrames[index].texture.get();
	}

	void drawTextureOpaque(HDC hdc, const Texture& texture, const RECT& dest) noexcept
	{
		if (!hdc || texture.empty() || dest.right <= dest.left || dest.bottom <= dest.top) {
			return;
		}

		BITMAPINFO bmpInfo;
		std::memset(&bmpInfo, 0, sizeof(bmpInfo));
		bmpInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
		bmpInfo.bmiHeader.biWidth = static_cast<LONG>(texture.width());
		bmpInfo.bmiHeader.biHeight = -static_cast<LONG>(texture.height());
		bmpInfo.bmiHeader.biPlanes = 1;
		bmpInfo.bmiHeader.biBitCount = 32;
		bmpInfo.bmiHeader.biCompression = BI_RGB;

		const auto previousStretchMode = SetStretchBltMode(hdc, HALFTONE);
		POINT previousBrushOrigin{};
		SetBrushOrgEx(hdc, 0, 0, &previousBrushOrigin);

		StretchDIBits(
			hdc,
			dest.left,
			dest.top,
			dest.right - dest.left,
			dest.bottom - dest.top,
			0,
			0,
			static_cast<int>(texture.width()),
			static_cast<int>(texture.height()),
			texture.pixels(),
			&bmpInfo,
			DIB_RGB_COLORS,
			SRCCOPY);

		SetBrushOrgEx(hdc, previousBrushOrigin.x, previousBrushOrigin.y, nullptr);
		if (previousStretchMode != 0) {
			SetStretchBltMode(hdc, previousStretchMode);
		}
	}

	RECT fitTextureRect(const Texture& texture, const RECT& bounds) noexcept
	{
		const auto boundsWidth = bounds.right - bounds.left;
		const auto boundsHeight = bounds.bottom - bounds.top;
		if (texture.empty() || boundsWidth <= 0 || boundsHeight <= 0) {
			return bounds;
		}

		const auto scale = (std::min)(
			static_cast<double>(boundsWidth) / (std::max)(1.0, static_cast<double>(texture.width())),
			static_cast<double>(boundsHeight) / (std::max)(1.0, static_cast<double>(texture.height())));
		const auto width = (std::max)(
			1,
			static_cast<int>(std::round(static_cast<double>(texture.width()) * scale)));
		const auto height = (std::max)(
			1,
			static_cast<int>(std::round(static_cast<double>(texture.height()) * scale)));
		const auto left = bounds.left + (boundsWidth - width) / 2;
		const auto top = bounds.top + (boundsHeight - height) / 2;
		return RECT{ left, top, left + width, top + height };
	}

	void drawTextureAlphaOnSolidBackground(
		HDC hdc,
		const Texture& texture,
		const RECT& dest,
		COLORREF backgroundColor) noexcept
	{
		if (!hdc || texture.empty() || dest.right <= dest.left || dest.bottom <= dest.top) {
			return;
		}

		const auto destWidth = static_cast<int>(dest.right - dest.left);
		const auto destHeight = static_cast<int>(dest.bottom - dest.top);
		std::vector<Texture::Pixel> pixels(
			static_cast<size_t>(destWidth) * static_cast<size_t>(destHeight));

		const auto backRed = static_cast<int>(GetRValue(backgroundColor));
		const auto backGreen = static_cast<int>(GetGValue(backgroundColor));
		const auto backBlue = static_cast<int>(GetBValue(backgroundColor));

		for (int y = 0; y < destHeight; ++y) {
			const auto sourceY = static_cast<uint32_t>(
				(static_cast<uint64_t>(y) * texture.height())
				/ static_cast<uint64_t>((std::max)(1, destHeight)));
			for (int x = 0; x < destWidth; ++x) {
				const auto sourceX = static_cast<uint32_t>(
					(static_cast<uint64_t>(x) * texture.width())
					/ static_cast<uint64_t>((std::max)(1, destWidth)));
				const auto source = texture.getPixel(sourceX, sourceY);
				const auto alpha = texture.hasAlpha()
					? static_cast<int>((source >> 24) & 0xff)
					: 255;

				const auto sourceBlue = static_cast<int>(source & 0xff);
				const auto sourceGreen = static_cast<int>((source >> 8) & 0xff);
				const auto sourceRed = static_cast<int>((source >> 16) & 0xff);

				const auto invAlpha = 255 - alpha;
				const auto red = (sourceRed * alpha + backRed * invAlpha) / 255;
				const auto green = (sourceGreen * alpha + backGreen * invAlpha) / 255;
				const auto blue = (sourceBlue * alpha + backBlue * invAlpha) / 255;
				pixels[static_cast<size_t>(x) + static_cast<size_t>(y) * destWidth] =
					(static_cast<Texture::Pixel>(red) << 16)
					| (static_cast<Texture::Pixel>(green) << 8)
					| static_cast<Texture::Pixel>(blue);
			}
		}

		BITMAPINFO bmpInfo;
		std::memset(&bmpInfo, 0, sizeof(bmpInfo));
		bmpInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
		bmpInfo.bmiHeader.biWidth = destWidth;
		bmpInfo.bmiHeader.biHeight = -destHeight;
		bmpInfo.bmiHeader.biPlanes = 1;
		bmpInfo.bmiHeader.biBitCount = 32;
		bmpInfo.bmiHeader.biCompression = BI_RGB;

		StretchDIBits(
			hdc,
			dest.left,
			dest.top,
			destWidth,
			destHeight,
			0,
			0,
			destWidth,
			destHeight,
			pixels.data(),
			&bmpInfo,
			DIB_RGB_COLORS,
			SRCCOPY);
	}

	void playViewWeaponFireSound(const ViewWeapon& weapon)
	{
		if (!g_soundEffectsEnabled || weapon.fireSoundPath().empty()) {
			return;
		}

		std::string error;
		if (!SoundEffectPlayer::playOnce(weapon.fireSoundPath(), 100, &error)
			&& !g_soundEffectWarningShown) {
			MessageBox(
				g_hWnd,
				("Could not play weapon sound effect:\n" + error).c_str(),
				g_szAppTitle,
				MB_OK | MB_ICONWARNING);
			g_soundEffectWarningShown = true;
		}
	}

	bool startViewWeaponReload(ViewWeapon& weapon) noexcept
	{
		if (weapon.activeAnimationName() != "idle" || !weapon.canReload()) {
			return false;
		}

		g_weaponAutoReloadPending = false;
		weapon.setAnimationOrFallback("reload", "idle");
		const auto reloaded = weapon.reload();
		if (reloaded && theWorldMap != nullptr) {
			const auto weaponName = weapon.name().empty() ? "weapon" : weapon.name();
			pushEventMessage(formatEventMessage(
				theWorldMap->messageLog().weaponReload,
				prettifyEventName(weaponName),
				std::string()), false);
		}

		return reloaded;
	}

	void updatePendingViewWeaponReload() noexcept
	{
		if (!g_weaponAutoReloadPending || !the3DEngine || !the3DEngine->viewWeapon()) {
			return;
		}

		auto* weapon = the3DEngine->viewWeapon();
		if (weapon->activeAnimationName() != "idle") {
			return;
		}

		startViewWeaponReload(*weapon);
	}

	bool pollKey(int virtualKey) noexcept
	{
		return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
	}

	double normalizeAngleRadians(double radians) noexcept
	{
		while (radians <= -kPi) {
			radians += kPi * 2.0;
		}

		while (radians > kPi) {
			radians -= kPi * 2.0;
		}

		return radians;
	}

	double maxDouble(double lhs, double rhs) noexcept
	{
		return lhs > rhs ? lhs : rhs;
	}

	double clampDouble(double value, double minValue, double maxValue) noexcept
	{
		if (value < minValue) {
			return minValue;
		}

		if (value > maxValue) {
			return maxValue;
		}

		return value;
	}

	void triggerDamageFlash(double damage) noexcept
	{
		const auto extra = clampDouble(damage / 100.0, 0.0, 1.0) * 0.18;
		g_damageFlashSeconds = maxDouble(g_damageFlashSeconds, 0.24 + extra);
	}

	void updateDamageFlash(double deltaSeconds) noexcept
	{
		if (g_damageFlashSeconds <= 0.0 || deltaSeconds <= 0.0) {
			return;
		}

		g_damageFlashSeconds = maxDouble(0.0, g_damageFlashSeconds - deltaSeconds);
	}

	void drawDamageFlashOverlay(
		HDC targetDc,
		int videoPosX,
		int videoPosY,
		int renderWidth,
		int renderHeight) noexcept
	{
		if (!targetDc || g_damageFlashSeconds <= 0.0
			|| renderWidth <= 0 || renderHeight <= 0) {
			return;
		}

		MemoryDcHandle overlayDc(CreateCompatibleDC(targetDc));
		if (!overlayDc) {
			return;
		}

		BitmapHandle overlayBitmap(CreateCompatibleBitmap(targetDc, 1, 1));
		if (!overlayBitmap) {
			return;
		}

		SelectObjectScope selectBitmap(overlayDc.get(), overlayBitmap.get());
		RECT overlayRect{ 0, 0, 1, 1 };
		BrushHandle redBrush(CreateSolidBrush(RGB(255, 0, 0)));
		if (!redBrush) {
			return;
		}

		FillRect(overlayDc.get(), &overlayRect, redBrush.get());

		const auto strength = clampDouble(g_damageFlashSeconds / 0.42, 0.0, 1.0);
		BLENDFUNCTION blend{};
		blend.BlendOp = AC_SRC_OVER;
		blend.SourceConstantAlpha =
			static_cast<BYTE>(std::round(34.0 + strength * 112.0));

		AlphaBlend(
			targetDc,
			videoPosX,
			videoPosY,
			renderWidth,
			renderHeight,
			overlayDc.get(),
			0,
			0,
			1,
			1,
			blend);
	}

	double playerDeathOverlayStrength() noexcept
	{
		if (g_playerLifeState == PlayerLifeState::Alive) {
			return 0.0;
		}

		const auto fade = clampDouble(
			g_playerDeathElapsedSeconds / kPlayerDeathFadeSeconds,
			0.0,
			1.0);
		return g_playerLifeState == PlayerLifeState::Respawning ? 1.0 : fade;
	}

	void drawPlayerDeathOverlay(
		HDC targetDc,
		int videoPosX,
		int videoPosY,
		int renderWidth,
		int renderHeight) noexcept
	{
		const auto strength = playerDeathOverlayStrength();
		if (!targetDc || strength <= 0.0 || renderWidth <= 0 || renderHeight <= 0) {
			return;
		}

		MemoryDcHandle overlayDc(CreateCompatibleDC(targetDc));
		if (!overlayDc) {
			return;
		}

		BitmapHandle overlayBitmap(CreateCompatibleBitmap(targetDc, 1, 1));
		if (!overlayBitmap) {
			return;
		}

		SelectObjectScope selectBitmap(overlayDc.get(), overlayBitmap.get());
		RECT overlayRect{ 0, 0, 1, 1 };
		BrushHandle redBrush(CreateSolidBrush(RGB(170, 0, 0)));
		if (!redBrush) {
			return;
		}
		FillRect(overlayDc.get(), &overlayRect, redBrush.get());

		BLENDFUNCTION blend{};
		blend.BlendOp = AC_SRC_OVER;
		blend.SourceConstantAlpha =
			static_cast<BYTE>(std::round(40.0 + strength * 180.0));
		AlphaBlend(
			targetDc,
			videoPosX,
			videoPosY,
			renderWidth,
			renderHeight,
			overlayDc.get(),
			0,
			0,
			1,
			1,
			blend);
	}

	int normalizeRay(int ray, const Player& player) noexcept
	{
		ray %= player.deg360();
		return ray < 0 ? ray + player.deg360() : ray;
	}

	double rayToRadians(const Player& player, int ray) noexcept
	{
		return static_cast<double>(normalizeRay(ray, player))
			/ static_cast<double>(player.deg360())
			* kPi * 2.0;
	}

	int radiansToRay(const Player& player, double radians) noexcept
	{
		while (radians < 0.0) {
			radians += kPi * 2.0;
		}

		while (radians >= kPi * 2.0) {
			radians -= kPi * 2.0;
		}

		return normalizeRay(
			static_cast<int>(std::round(
				radians * static_cast<double>(player.deg360()) / (kPi * 2.0))),
			player);
	}

	bool solidWallBlocksShot(double targetDistance, int targetRay) noexcept
	{
		if (!theWorldMap || !the3DEngine) {
			return false;
		}

		theWorldMap->setPlayerPos(
			the3DEngine->player().getX(),
			the3DEngine->player().getY());

		const auto hit = RayCaster(the3DEngine->player())
			.castSolidWallRay(*theWorldMap, targetRay);
		return hit.found && hit.distance + 1.0 < targetDistance;
	}

	bool worldCellBlocksLineOfSight(double x, double y) noexcept
	{
		if (!theWorldMap) {
			return false;
		}

		if (x < 0.0 || y < 0.0
			|| x >= theWorldMap->getMaxX()
			|| y >= theWorldMap->getMaxY()) {
			return true;
		}

		const auto column = static_cast<int>(x / theWorldMap->getCellDx());
		const auto row = static_cast<int>(y / theWorldMap->getCellDy());
		const auto* block = theWorldMap->blockAtCell(row, column);
		if (block != nullptr) {
			if (block->door.enabled) {
				return block->door.blocksWhenClosed
					&& !theWorldMap->isDoorOpenAt(row, column);
			}

			return block->hasAnySolidSpan;
		}

		return MapCell::hasSolidWall(theWorldMap->cellAtWorld(x, y));
	}

	bool solidWallBlocksSegment(
		double originX,
		double originY,
		double targetX,
		double targetY) noexcept
	{
		if (!theWorldMap) {
			return false;
		}

		const auto dx = targetX - originX;
		const auto dy = targetY - originY;
		const auto distance = std::sqrt(dx * dx + dy * dy);
		if (distance <= 1.0) {
			return false;
		}

		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;
		const auto step = maxDouble(1.0, cellSize * 0.125);
		const auto stepX = dx / distance * step;
		const auto stepY = dy / distance * step;
		const auto sampleCount =
			static_cast<int>(std::floor(distance / step));

		for (int sample = 1; sample < sampleCount; ++sample) {
			if (worldCellBlocksLineOfSight(
				originX + stepX * static_cast<double>(sample),
				originY + stepY * static_cast<double>(sample))) {
				return true;
			}
		}

		return false;
	}

	double targetDistanceAlongSegment(
		double originX,
		double originY,
		double targetX,
		double targetY,
		double segmentEndX,
		double segmentEndY,
		double hitRadius) noexcept
	{
		const auto segmentX = segmentEndX - originX;
		const auto segmentY = segmentEndY - originY;
		const auto segmentLength = std::sqrt(
			segmentX * segmentX + segmentY * segmentY);
		if (segmentLength <= 1.0) {
			return std::numeric_limits<double>::infinity();
		}

		const auto directionX = segmentX / segmentLength;
		const auto directionY = segmentY / segmentLength;
		const auto relativeX = targetX - originX;
		const auto relativeY = targetY - originY;
		const auto along = relativeX * directionX + relativeY * directionY;
		if (along <= 1.0 || along >= segmentLength) {
			return std::numeric_limits<double>::infinity();
		}

		const auto perpendicularX = relativeX - directionX * along;
		const auto perpendicularY = relativeY - directionY * along;
		const auto radius = maxDouble(1.0, hitRadius);
		return perpendicularX * perpendicularX + perpendicularY * perpendicularY
			<= radius * radius
			? along
			: std::numeric_limits<double>::infinity();
	}

	void storeRuntimeActorState(const SpriteActor& actor) noexcept
	{
		if (actor.persistenceKey.empty() || !the3DEngine) {
			return;
		}

		const auto* sprite = the3DEngine->sprite(actor.spriteIndex);
		if (sprite == nullptr) {
			return;
		}

		g_runtimeActorStateByKey[actor.persistenceKey] = RuntimeActorState{
			sprite->x,
			sprite->y,
			sprite->facingRadians,
			actor.health,
			actor.dead || (actor.maxHealth > 0.0 && actor.health <= 0.0),
			sprite->visible,
			actor.deathAnimationStarted
		};
	}

	void syncRuntimeActorStates() noexcept
	{
		for (const auto& actor : g_spriteActors) {
			storeRuntimeActorState(actor);
		}
	}

	void applyRuntimeActorState(
		SpriteActor& actor,
		Sprite& sprite,
		const RuntimeActorState& state) noexcept
	{
		sprite.x = state.x;
		sprite.y = state.y;
		sprite.facingRadians = state.facingRadians;
		sprite.visible = state.visible;
		actor.health = actor.maxHealth > 0.0
			? clampDouble(state.health, 0.0, actor.maxHealth)
			: state.health;
		actor.dead = state.dead || (actor.maxHealth > 0.0 && actor.health <= 0.0);
		actor.deathAnimationStarted = state.deathAnimationStarted;

		if (!actor.dead) {
			return;
		}

		actor.health = 0.0;
		actor.state = ActorState::Idle;
		actor.collidesWithWorld = false;
		if (sprite.setAnimation("death")) {
			actor.deathAnimationStarted = true;
			sprite.advanceAnimation(999.0);
			sprite.visible = true;
		}
		else {
			sprite.visible = false;
		}
	}

	void startDeathAnimation(SpriteActor& actor, Sprite& sprite) noexcept
	{
		actor.dead = true;
		actor.state = ActorState::Idle;
		actor.collidesWithWorld = false;
		actor.health = 0.0;

		if (actor.deathAnimationStarted) {
			return;
		}

		if (sprite.setAnimation("death")) {
			actor.deathAnimationStarted = true;
		}
		else {
			sprite.visible = false;
		}

		storeRuntimeActorState(actor);
	}

	double weaponNoiseRadiusCellsForActor(
		const ViewWeapon& weapon,
		const SpriteActor& actor) noexcept
	{
		const auto detectionRadius = maxDouble(0.0, actor.detectionRadiusCells);
		const auto attackRadius = maxDouble(0.0, actor.attackRangeCells);
		const auto weaponRadius = maxDouble(0.0, weapon.rangeCells());
		const auto baseRadius =
			maxDouble(detectionRadius, maxDouble(attackRadius, weaponRadius));
		const auto radius = maxDouble(
			kWeaponNoiseMinimumRadiusCells,
			maxDouble(
				detectionRadius * kWeaponNoiseRadiusMultiplier,
				baseRadius + kWeaponNoiseExtraRadiusCells));
		return clampDouble(
			radius,
			kWeaponNoiseMinimumRadiusCells,
			kWeaponNoiseMaximumRadiusCells);
	}

	void alertActorsFromWeaponNoise(const ViewWeapon& weapon) noexcept
	{
		if (!the3DEngine || !theWorldMap || g_spriteActors.empty()) {
			return;
		}

		const auto& player = the3DEngine->player();
		const auto playerX = static_cast<double>(player.getX());
		const auto playerY = static_cast<double>(player.getY());
		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;
		if (cellSize <= 0.0) {
			return;
		}

		for (auto& actor : g_spriteActors) {
			if (!actor.chasePlayer
				|| actor.dead
				|| actor.maxHealth <= 0.0
				|| actor.health <= 0.0) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - playerX;
			const auto dy = sprite->y - playerY;
			const auto distanceCells = std::sqrt(dx * dx + dy * dy) / cellSize;
			const auto noiseRadiusCells =
				weaponNoiseRadiusCellsForActor(weapon, actor);
			if (distanceCells > noiseRadiusCells) {
				continue;
			}

			actor.noiseAlertSecondsRemaining = maxDouble(
				actor.noiseAlertSecondsRemaining,
				kWeaponNoiseAlertSeconds);
			actor.noiseAlertRadiusCells = maxDouble(
				actor.noiseAlertRadiusCells,
				noiseRadiusCells + actor.engagementHysteresisCells);
			actor.state = ActorState::Chasing;
		}
	}

	void triggerRuntimeExplosion(RuntimeSpriteInfo& info) noexcept;
	void beginPlayerDeath() noexcept;

	bool isRuntimeDamageReactive(const RuntimeSpriteInfo& info) noexcept
	{
		return info.explosive || !info.damageResponseType.empty();
	}

	bool isRuntimeExplosionResponse(const RuntimeSpriteInfo& info) noexcept
	{
		return info.explosive
			|| equalsIgnoreCase(info.damageResponseType, "explode")
			|| equalsIgnoreCase(info.damageResponseType, "explosion");
	}

	bool playerMovementHitsObstacle(
		double previousX,
		double previousY,
		double destinationX,
		double destinationY) noexcept
	{
		if (!the3DEngine || !theWorldMap) {
			return false;
		}

		const auto playerRadius = maxDouble(
			4.0,
			(std::min)(theWorldMap->getCellDx(), theWorldMap->getCellDy()) / 8.0);
		const auto movementHitsCircle = [=](const Sprite& sprite, double radius) {
			const auto minimumDistance = playerRadius + radius;
			const auto minimumDistanceSquared = minimumDistance * minimumDistance;
			const auto previousDx = sprite.x - previousX;
			const auto previousDy = sprite.y - previousY;
			const auto destinationDx = sprite.x - destinationX;
			const auto destinationDy = sprite.y - destinationY;
			const auto previousDistanceSquared =
				previousDx * previousDx + previousDy * previousDy;
			const auto destinationDistanceSquared =
				destinationDx * destinationDx + destinationDy * destinationDy;
			return destinationDistanceSquared < minimumDistanceSquared
				&& (previousDistanceSquared >= minimumDistanceSquared
					|| destinationDistanceSquared <= previousDistanceSquared);
		};

		for (const auto& info : g_runtimeSpriteInfos) {
			if (!info.blocksPlayer
				|| info.consumed
				|| info.explosionActive) {
				continue;
			}

			const auto* sprite = the3DEngine->sprite(info.spriteIndex);
			if (sprite == nullptr || !sprite->visible || sprite->collisionRadius <= 0.0) {
				continue;
			}

			if (movementHitsCircle(*sprite, sprite->collisionRadius)) {
				return true;
			}
		}

		for (const auto& actor : g_spriteActors) {
			if (actor.dead || (actor.maxHealth > 0.0 && actor.health <= 0.0)) {
				continue;
			}

			const auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto radius = maxDouble(sprite->collisionRadius, sprite->scale * 0.28);
			if (movementHitsCircle(*sprite, radius)) {
				return true;
			}
		}

		return false;
	}

	Player::Cell movePlayerRespectingDestructibleProps(int offset, int degrees = 0)
	{
		if (!the3DEngine || !theWorldMap) {
			return 0;
		}

		auto& player = the3DEngine->player();
		const auto previousPosition = player.getPosition();
		const auto destinationCell = player.moveTo(offset, *theWorldMap, degrees);
		const auto destinationPosition = player.getPosition();
		if (!playerMovementHitsObstacle(
			previousPosition.first,
			previousPosition.second,
			destinationPosition.first,
			destinationPosition.second)) {
			return destinationCell;
		}

		player.setPos(previousPosition);
		const auto row = player.getRow(theWorldMap->getCellDy());
		const auto column = player.getCol(theWorldMap->getCellDx());
		return row >= 0 && column >= 0
			&& row < theWorldMap->getRowCount()
			&& column < theWorldMap->getColCount()
			? (*theWorldMap)[row][column]
			: 0;
	}

	void destroyActor(SpriteActor& actor, Sprite& sprite) noexcept
	{
		for (auto& info : g_runtimeSpriteInfos) {
			if (info.spriteIndex != actor.spriteIndex
				|| !isRuntimeExplosionResponse(info)
				|| info.consumed
				|| info.explosionActive) {
				continue;
			}

			triggerRuntimeExplosion(info);
			return;
		}

		startDeathAnimation(actor, sprite);
	}

	std::string runtimeDamageEffectAnimation(const RuntimeSpriteInfo& info)
	{
		if (!info.damageEffectAnimation.empty()) {
			return info.damageEffectAnimation;
		}

		if (equalsIgnoreCase(info.damageResponseType, "break")) {
			return "break";
		}

		return "explode";
	}

	std::string runtimeDamageEffectSound(const RuntimeSpriteInfo& info)
	{
		if (!info.damageEffectSound.empty()) {
			return info.damageEffectSound;
		}

		return isRuntimeExplosionResponse(info)
			? std::string(kExplosionSoundPath)
			: std::string();
	}

	void damageRuntimeSprite(
		RuntimeSpriteInfo& info,
		double damage) noexcept
	{
		if (!isRuntimeDamageReactive(info)
			|| info.consumed
			|| info.explosionActive
			|| damage <= 0.0) {
			return;
		}

		info.explosiveHealth = maxDouble(
			0.0,
			info.explosiveHealth - damage);
		if (!info.persistenceKey.empty()) {
			g_runtimeSpriteExplosiveHealthByKey[info.persistenceKey] =
				info.explosiveHealth;
		}

		if (info.explosiveHealth <= 0.0) {
			triggerRuntimeExplosion(info);
		}
	}

	void applyViewWeaponDamage() noexcept
	{
		if (!the3DEngine || !theWorldMap || !the3DEngine->viewWeapon()) {
			return;
		}

		const auto* weapon = the3DEngine->viewWeapon();
		if (weapon->damage() <= 0.0 || weapon->rangeCells() <= 0.0) {
			return;
		}

		const auto& player = the3DEngine->player();
		const auto playerX = static_cast<double>(player.getX());
		const auto playerY = static_cast<double>(player.getY());
		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;
		const auto range = weapon->rangeCells() * cellSize;
		const auto cameraRay = normalizeRay(
			player.getAlpha() + player.degHalfVisual(),
			player);
		const auto cameraRadians = rayToRadians(player, cameraRay);

		SpriteActor* bestActor = nullptr;
		RuntimeSpriteInfo* bestExplosiveInfo = nullptr;
		Sprite* bestSprite = nullptr;
		auto bestDistance = std::numeric_limits<double>::infinity();
		auto bestAngularError = std::numeric_limits<double>::infinity();

		for (auto& actor : g_spriteActors) {
			if (actor.dead || actor.maxHealth <= 0.0 || actor.health <= 0.0) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - playerX;
			const auto dy = sprite->y - playerY;
			const auto distance = std::sqrt(dx * dx + dy * dy);
			if (distance <= 1.0 || distance > range) {
				continue;
			}

			const auto spriteRadians = std::atan2(dy, dx);
			const auto angularError =
				std::abs(normalizeAngleRadians(spriteRadians - cameraRadians));
			const auto apparentHalfWidth = maxDouble(
				kPi / 90.0,
				std::atan2(
					maxDouble(sprite->collisionRadius, sprite->scale * 0.22),
					distance));
			if (angularError > apparentHalfWidth) {
				continue;
			}

			const auto targetRay = radiansToRay(player, spriteRadians);
			if (solidWallBlocksShot(distance, targetRay)) {
				continue;
			}

			if (distance < bestDistance
				|| (std::abs(distance - bestDistance) < 1.0
					&& angularError < bestAngularError)) {
				bestActor = &actor;
				bestExplosiveInfo = nullptr;
				bestSprite = sprite;
				bestDistance = distance;
				bestAngularError = angularError;
			}
		}

		for (auto& info : g_runtimeSpriteInfos) {
			if (!isRuntimeDamageReactive(info)
				|| info.consumed
				|| info.explosionActive) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(info.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - playerX;
			const auto dy = sprite->y - playerY;
			const auto distance = std::sqrt(dx * dx + dy * dy);
			if (distance <= 1.0 || distance > range) {
				continue;
			}

			const auto spriteRadians = std::atan2(dy, dx);
			const auto angularError =
				std::abs(normalizeAngleRadians(spriteRadians - cameraRadians));
			const auto apparentHalfWidth = maxDouble(
				kPi / 110.0,
				std::atan2(
					maxDouble(sprite->collisionRadius, sprite->scale * 0.28),
					distance));
			if (angularError > apparentHalfWidth) {
				continue;
			}

			const auto targetRay = radiansToRay(player, spriteRadians);
			if (solidWallBlocksShot(distance, targetRay)) {
				continue;
			}

			if (distance < bestDistance
				|| (std::abs(distance - bestDistance) < 1.0
					&& angularError < bestAngularError)) {
				bestActor = nullptr;
				bestExplosiveInfo = &info;
				bestSprite = sprite;
				bestDistance = distance;
				bestAngularError = angularError;
			}
		}

		if (bestExplosiveInfo != nullptr) {
			damageRuntimeSprite(*bestExplosiveInfo, weapon->damage());
			return;
		}

		if (bestActor == nullptr || bestSprite == nullptr) {
			return;
		}

		bestActor->health = maxDouble(0.0, bestActor->health - weapon->damage());
		if (bestActor->health <= 0.0) {
			destroyActor(*bestActor, *bestSprite);
		}
		else {
			storeRuntimeActorState(*bestActor);
		}
	}

	bool isComputerPickup(const RuntimeSpriteInfo& info) noexcept
	{
		return info.unlocksMap
			|| containsIgnoreCase(info.spriteSet, "computer")
			|| containsIgnoreCase(info.name, "computer");
	}

	bool isMedikitPickup(const RuntimeSpriteInfo& info) noexcept
	{
		return info.pickupHealth > 0.0
			|| containsIgnoreCase(info.spriteSet, "medikit")
			|| containsIgnoreCase(info.spriteSet, "medic")
			|| containsIgnoreCase(info.name, "medikit")
			|| containsIgnoreCase(info.name, "medic");
	}

	bool isAmmoPickup(const RuntimeSpriteInfo& info) noexcept
	{
		return containsIgnoreCase(info.spriteSet, "ammo")
			|| containsIgnoreCase(info.name, "ammo");
	}

	bool isKeyPickup(const RuntimeSpriteInfo& info) noexcept
	{
		return containsIgnoreCase(info.spriteSet, "item_key")
			|| containsIgnoreCase(info.spriteSet, "key_")
			|| containsIgnoreCase(info.name, "key");
	}

	bool isKeyPickupIdentity(
		const std::string& name,
		const std::string& spriteSet) noexcept
	{
		return containsIgnoreCase(spriteSet, "item_key")
			|| containsIgnoreCase(spriteSet, "key_")
			|| containsIgnoreCase(name, "key");
	}

	std::string keyIdFromText(const std::string& text)
	{
		const auto lower = lowerCopy(text);
		if (lower.find("green") != std::string::npos) {
			return "green";
		}

		if (lower.find("blue") != std::string::npos) {
			return "blue";
		}

		if (lower.find("red") != std::string::npos) {
			return "red";
		}

		if (lower.find("yellow") != std::string::npos) {
			return "yellow";
		}

		return {};
	}

	std::string keyIdFromSpriteIdentity(
		const std::string& name,
		const std::string& spriteSet)
	{
		auto keyId = keyIdFromText(spriteSet);
		return keyId.empty() ? keyIdFromText(name) : keyId;
	}

	bool playerHasKeyId(const std::string& keyId) noexcept
	{
		if (keyId.empty()) {
			return false;
		}

		return std::find(
			g_playerKeyIds.begin(),
			g_playerKeyIds.end(),
			keyId) != g_playerKeyIds.end();
	}

	void syncWorldDoorKeyring()
	{
		if (theWorldMap) {
			theWorldMap->setDoorKeyring(g_playerKeyIds);
		}
	}

	void addPlayerKeyId(const std::string& keyId)
	{
		if (keyId.empty() || playerHasKeyId(keyId)) {
			return;
		}

		g_playerKeyIds.push_back(keyId);
		syncWorldDoorKeyring();
	}

	std::string runtimeSpritePersistenceKey(
		const std::string& worldPath,
		const std::string& layerId,
		const SceneLoader::SpriteInstance& instance)
	{
		auto key = worldPath + "|" + layerId + "|";
		if (!instance.name.empty()) {
			return key + instance.name;
		}

		return key
			+ instance.spriteSet
			+ "@"
			+ formatDouble(instance.xCell, 3)
			+ ","
			+ formatDouble(instance.yCell, 3);
	}

	void markRuntimeSpriteConsumed(const RuntimeSpriteInfo& info)
	{
		if (!info.persistenceKey.empty()) {
			g_runtimeSpriteConsumedByKey[info.persistenceKey] = true;
		}
	}

	void markRuntimeSpriteExploded(const RuntimeSpriteInfo& info)
	{
		if (!info.persistenceKey.empty()) {
			g_runtimeSpriteConsumedByKey[info.persistenceKey] = true;
			g_runtimeSpriteExplodedByKey[info.persistenceKey] = true;
			g_runtimeSpriteExplosiveHealthByKey[info.persistenceKey] = 0.0;
		}
	}

	void syncRuntimeSpriteStates()
	{
		for (const auto& info : g_runtimeSpriteInfos) {
			if (info.persistenceKey.empty()) {
				continue;
			}

			if (isRuntimeDamageReactive(info)) {
				g_runtimeSpriteExplosiveHealthByKey[info.persistenceKey] =
					clampDouble(
						info.explosiveHealth,
						0.0,
						maxDouble(1.0, info.explosiveHitPoints));
			}

			if (!info.consumed) {
				continue;
			}

			g_runtimeSpriteConsumedByKey[info.persistenceKey] = true;
			if (isRuntimeDamageReactive(info)
				&& (info.explosiveHealth <= 0.0
					|| info.explosionActive)) {
				g_runtimeSpriteExplodedByKey[info.persistenceKey] = true;
				g_runtimeSpriteExplosiveHealthByKey[info.persistenceKey] = 0.0;
			}
		}
	}

	bool isValidSpriteIndex(size_t spriteIndex) noexcept
	{
		return spriteIndex != kInvalidSpriteIndex
			&& the3DEngine
			&& spriteIndex < the3DEngine->sprites().size();
	}

	double animationDurationSeconds(
		const Sprite& sprite,
		const std::string& animationName,
		double fallbackSeconds) noexcept
	{
		const auto* clip = sprite.animation(animationName);
		if (clip == nullptr
			|| clip->frameDurationMs <= 0.0
			|| clip->frameSets.empty()) {
			return fallbackSeconds;
		}

		return maxDouble(
			0.05,
			(clip->frameDurationMs / 1000.0)
			* static_cast<double>(clip->frameSets.size()));
	}

	void triggerRuntimeExplosion(RuntimeSpriteInfo& info) noexcept;

	void applyExplosionDamage(
		const RuntimeSpriteInfo& sourceInfo,
		double originX,
		double originY) noexcept
	{
		if (!the3DEngine || !theWorldMap) {
			return;
		}

		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;
		if (cellSize <= 0.0) {
			return;
		}

		const auto radiusCells = maxDouble(0.1, sourceInfo.explosionRadiusCells);
		const auto radius = radiusCells * cellSize;
		const auto baseDamage = maxDouble(0.0, sourceInfo.explosionDamage);
		if (baseDamage <= 0.0) {
			return;
		}

		auto damageAtDistance = [&](double distance) noexcept {
			const auto distanceCells = distance / cellSize;
			const auto falloff =
				clampDouble(1.0 - distanceCells / radiusCells, 0.0, 1.0);
			return baseDamage * falloff;
			};

		for (auto& actor : g_spriteActors) {
			if (actor.dead || actor.maxHealth <= 0.0 || actor.health <= 0.0) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - originX;
			const auto dy = sprite->y - originY;
			const auto distance = std::sqrt(dx * dx + dy * dy);
			if (distance > radius) {
				continue;
			}

			const auto damage = damageAtDistance(distance);
			if (damage <= 0.0) {
				continue;
			}

			actor.health = maxDouble(0.0, actor.health - damage);
			if (actor.health <= 0.0) {
				destroyActor(actor, *sprite);
			}
			else {
				storeRuntimeActorState(actor);
			}
		}

		const auto& player = the3DEngine->player();
		const auto playerDx = static_cast<double>(player.getX()) - originX;
		const auto playerDy = static_cast<double>(player.getY()) - originY;
		const auto playerDistance =
			std::sqrt(playerDx * playerDx + playerDy * playerDy);
		if (playerDistance <= radius) {
			const auto damage = damageAtDistance(playerDistance);
			if (damage > 0.0) {
				if (!g_playerImmortal) {
					g_playerCombatStats.health = maxDouble(
						0.0,
						g_playerCombatStats.health - damage);
				}

				triggerDamageFlash(damage);
				if (g_playerCombatStats.health <= 0.0) {
					beginPlayerDeath();
				}
			}
		}

		std::vector<std::pair<size_t, double>> damagedSprites;
		for (size_t index = 0; index < g_runtimeSpriteInfos.size(); ++index) {
			auto& candidate = g_runtimeSpriteInfos[index];
			if (&candidate == &sourceInfo
				|| !isRuntimeDamageReactive(candidate)
				|| candidate.consumed
				|| candidate.explosionActive) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(candidate.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - originX;
			const auto dy = sprite->y - originY;
			const auto distance = std::sqrt(dx * dx + dy * dy);
			if (distance <= radius) {
				const auto damage = damageAtDistance(distance);
				if (damage > 0.0) {
					damagedSprites.emplace_back(index, damage);
				}
			}
		}

		for (const auto& [index, damage] : damagedSprites) {
			if (index < g_runtimeSpriteInfos.size()) {
				damageRuntimeSprite(g_runtimeSpriteInfos[index], damage);
			}
		}
	}

	void triggerRuntimeExplosion(RuntimeSpriteInfo& info) noexcept
	{
		if (!the3DEngine
			|| !isRuntimeDamageReactive(info)
			|| info.consumed
			|| info.explosionActive) {
			return;
		}

		auto* sourceSprite = the3DEngine->sprite(info.spriteIndex);
		if (sourceSprite == nullptr || !sourceSprite->visible) {
			return;
		}

		const auto originX = sourceSprite->x;
		const auto originY = sourceSprite->y;
		sourceSprite->visible = false;
		info.consumed = true;
		info.explosiveHealth = 0.0;
		markRuntimeSpriteExploded(info);

		for (auto& actor : g_spriteActors) {
			if (actor.spriteIndex != info.spriteIndex) {
				continue;
			}

			actor.dead = true;
			actor.state = ActorState::Idle;
			actor.collidesWithWorld = false;
			actor.health = 0.0;
			actor.deathAnimationStarted = true;
			storeRuntimeActorState(actor);
			break;
		}

		if (auto* destroyed = the3DEngine->sprite(info.destroyedSpriteIndex)) {
			destroyed->x = originX;
			destroyed->y = originY;
			destroyed->visible = false;
		}

		if (auto* explosion = the3DEngine->sprite(info.explosionSpriteIndex)) {
			const auto effectAnimation = runtimeDamageEffectAnimation(info);
			explosion->x = originX;
			explosion->y = originY;
			explosion->verticalOffset = sourceSprite->verticalOffset;
			explosion->visible = true;
			explosion->setAnimationOrFallback(effectAnimation, "idle");
			explosion->animationTimeSeconds = 0.0;
			explosion->animationFrameIndex = 0;
			info.explosionActive = true;
			info.explosionElapsedSeconds = 0.0;
			info.explosionDurationSeconds =
				animationDurationSeconds(*explosion, effectAnimation, 0.65);
		}

		if (!info.explosionActive) {
			if (auto* destroyed = the3DEngine->sprite(info.destroyedSpriteIndex)) {
				destroyed->visible = true;
			}
		}

		const auto effectSound = runtimeDamageEffectSound(info);
		if (!effectSound.empty()) {
			playWorldSoundEffect(effectSound.c_str());
		}

		if (isRuntimeExplosionResponse(info)
			&& (info.explosionRadiusCells > 0.0 || info.explosionDamage > 0.0)) {
			applyExplosionDamage(info, originX, originY);
		}
	}

	void updateRuntimeExplosions(double deltaSeconds) noexcept
	{
		if (!the3DEngine || deltaSeconds <= 0.0) {
			return;
		}

		for (auto& info : g_runtimeSpriteInfos) {
			if (!info.explosionActive) {
				continue;
			}

			info.explosionElapsedSeconds += deltaSeconds;
			if (info.explosionElapsedSeconds < info.explosionDurationSeconds) {
				continue;
			}

			if (auto* explosion = the3DEngine->sprite(info.explosionSpriteIndex)) {
				explosion->visible = false;
			}

			if (auto* destroyed = the3DEngine->sprite(info.destroyedSpriteIndex)) {
				destroyed->visible = true;
			}

			info.explosionActive = false;
		}
	}

	bool isPlayerStandingOnProp(const char* spriteIdentity, double radiusCells) noexcept
	{
		if (!the3DEngine || !theWorldMap || spriteIdentity == nullptr) {
			return false;
		}

		const auto& player = the3DEngine->player();
		const auto playerX = static_cast<double>(player.getX());
		const auto playerY = static_cast<double>(player.getY());
		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;
		const auto radius = maxDouble(1.0, radiusCells * cellSize);

		for (const auto& info : g_runtimeSpriteInfos) {
			if (info.consumed
				|| (!containsIgnoreCase(info.spriteSet, spriteIdentity)
					&& !containsIgnoreCase(info.name, spriteIdentity))) {
				continue;
			}

			const auto* sprite = the3DEngine->sprite(info.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - playerX;
			const auto dy = sprite->y - playerY;
			if (std::sqrt(dx * dx + dy * dy) <= radius) {
				return true;
			}
		}

		return false;
	}

	void updatePlayerPropViewLift() noexcept
	{
		if (!the3DEngine || g_elevatorShake.active
			|| g_pendingLayerTransition.active) {
			return;
		}

		auto& player = the3DEngine->player();
		auto targetCenter = kPlayerBaseViewCenter;
		if (isPlayerStandingOnProp("supply_crate", kPlayerPropStandRadiusCells)
			|| isPlayerStandingOnProp("toolbox", kPlayerPropStandRadiusCells)) {
			targetCenter = kPlayerPropLiftViewCenter;
		}

		const auto currentCenter = player.getCenterProj();
		player.setCenterProj(currentCenter
			+ (targetCenter - currentCenter) * kPlayerPropLiftEase);
	}

	bool hasEquippedPlayerWeapon() noexcept;
	bool activatePlayerWeapon(size_t weaponIndex, bool syncCurrent);
	bool equipFirstUnlockedPlayerWeapon();
	bool playerWeaponFileMatches(
		const std::string& configuredFile,
		const std::string& requestedFile);
	bool unlockPlayerWeaponByFile(
		const std::string& weaponFile,
		size_t* unlockedIndex);
	void syncActivePlayerWeaponFromEngine() noexcept;

	COLORREF colorForKeyId(
		const std::string& keyId,
		COLORREF fallback = RGB(255, 238, 120))
	{
		const auto lower = lowerCopy(keyId);
		if (lower.find("green") != std::string::npos) {
			return RGB(74, 224, 100);
		}

		if (lower.find("blue") != std::string::npos) {
			return RGB(78, 158, 255);
		}

		if (lower.find("red") != std::string::npos) {
			return RGB(242, 74, 66);
		}

		if (lower.find("yellow") != std::string::npos) {
			return RGB(250, 218, 74);
		}

		return fallback;
	}

	const Texture* keyHudTextureForId(const std::string& keyId)
	{
		const auto lower = lowerCopy(keyId);
		if (lower.empty()) {
			return nullptr;
		}

		const auto it = g_keyHudTextures.find(lower);
		if (it != g_keyHudTextures.end()) {
			return it->second.get();
		}

		const auto path = firstExistingPath(
			currentWorldAssetBaseDir(),
			{
				"sprites/items/item_key_" + lower + "/256/front.png",
				"sprites/items/item_key_" + lower + "/128/front.png",
				"sprites/items/item_" + lower + "_key/256/front.png",
				"sprites/items/item_" + lower + "_key/128/front.png"
			});
		auto texture = loadTextureFromFile(path, 0, 0);
		auto* result = texture.get();
		g_keyHudTextures[lower] = std::move(texture);
		return result;
	}

	bool isCompletionItem(const RuntimeSpriteInfo& info) noexcept
	{
		return isKeyPickup(info)
			|| !info.pickupWeapon.empty()
			|| isComputerPickup(info)
			|| isAmmoPickup(info)
			|| isMedikitPickup(info);
	}

	bool isCompletionEnemy(const SpriteActor& actor) noexcept
	{
		return actor.maxHealth > 0.0 && actor.chasePlayer;
	}

	bool isActorSpriteIndex(size_t spriteIndex) noexcept
	{
		return std::any_of(
			g_spriteActors.begin(),
			g_spriteActors.end(),
			[spriteIndex](const SpriteActor& actor) {
				return actor.spriteIndex == spriteIndex;
			});
	}

	bool vectorContainsString(
		const std::vector<std::string>& values,
		const std::string& value) noexcept
	{
		return std::find(values.begin(), values.end(), value) != values.end();
	}

	bool missionEnemyKilled(const std::string& persistenceKey) noexcept
	{
		for (const auto& actor : g_spriteActors) {
			if (actor.persistenceKey != persistenceKey) {
				continue;
			}

			return actor.dead || actor.health <= 0.0;
		}

		const auto state = g_runtimeActorStateByKey.find(persistenceKey);
		return state != g_runtimeActorStateByKey.end()
			&& state->second.dead;
	}

	bool missionKeyCollected(const std::string& persistenceKey) noexcept
	{
		for (const auto& info : g_runtimeSpriteInfos) {
			if (info.persistenceKey == persistenceKey) {
				return info.consumed;
			}
		}

		const auto state = g_runtimeSpriteConsumedByKey.find(persistenceKey);
		return state != g_runtimeSpriteConsumedByKey.end()
			&& state->second;
	}

	bool missionPropDestroyed(const std::string& persistenceKey) noexcept
	{
		const auto state = g_runtimeSpriteExplodedByKey.find(persistenceKey);
		return state != g_runtimeSpriteExplodedByKey.end() && state->second;
	}

	GameCompletionStats currentCompletionStats() noexcept
	{
		GameCompletionStats stats;

		if (!g_missionObjectives.enemyPersistenceKeys.empty()
			|| !g_missionObjectives.keyPersistenceKeys.empty()
			|| !g_missionObjectives.itemPersistenceKeys.empty()
			|| !g_missionObjectives.destructiblePropPersistenceKeys.empty()) {
			stats.totalEnemies =
				static_cast<int>(g_missionObjectives.enemyPersistenceKeys.size());
			stats.totalKeys =
				static_cast<int>(g_missionObjectives.keyPersistenceKeys.size());
			stats.totalItems =
				static_cast<int>(g_missionObjectives.itemPersistenceKeys.size());
			stats.totalDestructibleProps = static_cast<int>(
				g_missionObjectives.destructiblePropPersistenceKeys.size());

			for (const auto& key : g_missionObjectives.enemyPersistenceKeys) {
				if (missionEnemyKilled(key)) {
					++stats.killedEnemies;
				}
			}

			for (const auto& key : g_missionObjectives.keyPersistenceKeys) {
				if (missionKeyCollected(key)) {
					++stats.collectedKeys;
				}
			}

			for (const auto& key : g_missionObjectives.itemPersistenceKeys) {
				if (missionKeyCollected(key)) {
					++stats.acquiredItems;
				}
			}

			for (const auto& key : g_missionObjectives.destructiblePropPersistenceKeys) {
				if (missionPropDestroyed(key)) {
					++stats.destroyedProps;
				}
			}

			return stats;
		}

		for (const auto& actor : g_spriteActors) {
			if (!isCompletionEnemy(actor)) {
				continue;
			}

			++stats.totalEnemies;
			if (actor.dead || actor.health <= 0.0) {
				++stats.killedEnemies;
			}
		}

		for (const auto& info : g_runtimeSpriteInfos) {
			if (isCompletionItem(info)) {
				++stats.totalItems;
				if (info.consumed) {
					++stats.acquiredItems;
				}
			}

			if (isRuntimeDamageReactive(info)
				&& !isCompletionItem(info)
				&& !isActorSpriteIndex(info.spriteIndex)) {
				++stats.totalDestructibleProps;
				if (info.consumed) {
					++stats.destroyedProps;
				}
			}

			if (!isKeyPickup(info)) {
				continue;
			}

			++stats.totalKeys;
			if (info.consumed) {
				++stats.collectedKeys;
			}
		}

		return stats;
	}

	int completionPercent(int done, int total) noexcept
	{
		if (total <= 0) {
			return 100;
		}

		return clampInt(
			static_cast<int>(std::round(
				static_cast<double>(done) * 100.0 / static_cast<double>(total))),
			0,
			100);
	}

	void saveAutoCheckpoint(bool announce) noexcept
	{
		if (!the3DEngine || g_playerCombatStats.health <= 0.0
			|| g_playerLifeState != PlayerLifeState::Alive || g_gameCompleted) {
			return;
		}

		syncRuntimeActorStates();
		syncRuntimeSpriteStates();
		syncActivePlayerWeaponFromEngine();

		const auto& player = the3DEngine->player();
		GameCheckpoint checkpoint;
		checkpoint.valid = true;
		checkpoint.layerId = g_activeLayerId;
		checkpoint.playerX = static_cast<double>(player.getX());
		checkpoint.playerY = static_cast<double>(player.getY());
		checkpoint.playerAlpha = player.getAlpha();
		checkpoint.playerSlope = player.getSlope();
		checkpoint.playerCenterProj = player.getCenterProj();
		checkpoint.combatStats = g_playerCombatStats;
		checkpoint.keyIds = g_playerKeyIds;
		checkpoint.minimapUnlocked = g_minimapUnlocked;
		checkpoint.minimapActorsUnlocked = g_minimapActorsUnlocked;
		checkpoint.runtimeSpriteConsumedByKey = g_runtimeSpriteConsumedByKey;
		checkpoint.runtimeSpriteExplodedByKey = g_runtimeSpriteExplodedByKey;
		checkpoint.runtimeSpriteExplosiveHealthByKey = g_runtimeSpriteExplosiveHealthByKey;
		checkpoint.runtimeActorStateByKey = g_runtimeActorStateByKey;

		checkpoint.weapons.reserve(g_playerWeapons.size());
		for (const auto& playerWeapon : g_playerWeapons) {
			PlayerWeaponCheckpoint weapon;
			weapon.file = playerWeapon.file;
			weapon.unlocked = playerWeapon.unlocked;
			weapon.usesAmmo = playerWeapon.weapon.usesAmmo();
			if (weapon.usesAmmo) {
				weapon.ammoInMagazine = playerWeapon.weapon.ammoInMagazine();
				weapon.reserveAmmo = playerWeapon.weapon.reserveAmmo();
			}
			checkpoint.weapons.push_back(weapon);
		}

		if (g_activePlayerWeaponIndex < g_playerWeapons.size()) {
			checkpoint.activeWeaponFile = g_playerWeapons[g_activePlayerWeaponIndex].file;
		}

		g_autoCheckpoint = std::move(checkpoint);
		if (announce) {
			pushEventMessage("Recovery data secured", true);
		}
	}

	std::string leaderboardFilePath()
	{
		const auto* localAppData = std::getenv("LOCALAPPDATA");
		const auto base = localAppData != nullptr && *localAppData != '\0'
			? std::string(localAppData)
			: g_currentWorldDir;
		const auto directory = joinPath(base, "nuRCADE");
		CreateDirectoryA(directory.c_str(), nullptr);
		return joinPath(directory, "leaderboard.json");
	}

	std::vector<LeaderboardEntry> loadLeaderboard()
	{
		std::vector<LeaderboardEntry> entries;
		std::ifstream input(leaderboardFilePath());
		if (!input.is_open()) {
			return entries;
		}

		try {
			nlohmann::json document;
			input >> document;
			if (document.is_array()) {
				for (const auto& item : document) {
					if (!item.is_object()) {
						continue;
					}

					entries.push_back({
						item.value("name", std::string("PLAYER")),
						item.value("score", 0),
						item.value("completionSeconds", 0.0)
					});
				}
			}
		}
		catch (...) {
			entries.clear();
		}

		std::sort(entries.begin(), entries.end(), [](const auto& left, const auto& right) {
			if (left.score != right.score) {
				return left.score > right.score;
			}
			return left.completionSeconds < right.completionSeconds;
		});
		if (entries.size() > 5) {
			entries.resize(5);
		}
		return entries;
	}

	void saveLeaderboard(const std::vector<LeaderboardEntry>& entries)
	{
		nlohmann::json document = nlohmann::json::array();
		for (const auto& entry : entries) {
			document.push_back({
				{ "name", entry.name },
				{ "score", entry.score },
				{ "completionSeconds", entry.completionSeconds }
			});
		}

		std::ofstream output(leaderboardFilePath(), std::ios::trunc);
		if (output.is_open()) {
			output << document.dump(2);
		}
	}

	bool completionScoreQualifies(const CompletionSummaryState& summary) noexcept
	{
		if (summary.leaderboard.size() < 5) {
			return true;
		}

		const auto& last = summary.leaderboard.back();
		return summary.totalScore > last.score
			|| (summary.totalScore == last.score
				&& summary.completionSeconds < last.completionSeconds);
	}

	void beginCompletionSummary()
	{
		g_completionSummary = {};
		auto& summary = g_completionSummary;
		summary.active = true;
		summary.stats = currentCompletionStats();
		summary.completionSeconds = g_missionElapsedSeconds;
		summary.enemyPoints = summary.stats.killedEnemies * 500;
		summary.itemPoints = summary.stats.acquiredItems * 100;
		summary.destructionPenalty = summary.stats.destroyedProps * 150;
		summary.timeBonus = (std::max)(
			0,
			1800 - static_cast<int>(std::floor(summary.completionSeconds))) * 5;
		summary.totalScore = (std::max)(
			0,
			5000 + summary.enemyPoints + summary.itemPoints
			+ summary.timeBonus - summary.destructionPenalty);
		summary.leaderboard = loadLeaderboard();
	}

	bool advanceCompletionCounter(int& displayed, int target)
	{
		if (displayed >= target) {
			return false;
		}

		const auto step = (std::max)(1, target / 24);
		displayed = (std::min)(target, displayed + step);
		return true;
	}

	void updateCompletionSummary(double deltaSeconds)
	{
		auto& summary = g_completionSummary;
		if (!summary.active || summary.countingComplete || deltaSeconds <= 0.0) {
			return;
		}

		summary.tickSoundCooldown = maxDouble(0.0, summary.tickSoundCooldown - deltaSeconds);
		bool changed = false;
		switch (summary.counterStage) {
		case 0:
			changed = advanceCompletionCounter(
				summary.displayedEnemies,
				summary.stats.killedEnemies);
			if (!changed) {
				++summary.counterStage;
			}
			break;
		case 1:
			changed = advanceCompletionCounter(
				summary.displayedItems,
				summary.stats.acquiredItems);
			if (!changed) {
				++summary.counterStage;
			}
			break;
		case 2:
			changed = advanceCompletionCounter(
				summary.displayedDestroyedProps,
				summary.stats.destroyedProps);
			if (!changed) {
				++summary.counterStage;
			}
			break;
		default:
			changed = advanceCompletionCounter(summary.displayedScore, summary.totalScore);
			if (!changed) {
				summary.countingComplete = true;
				summary.enteringName = completionScoreQualifies(summary);
				summary.awaitingRestart = !summary.enteringName;
			}
			break;
		}

		if (changed && summary.tickSoundCooldown <= 0.0) {
			playWorldSoundEffect(kEnemyRangedAttackSoundPath, 38);
			summary.tickSoundCooldown = 0.075;
		}
	}

	void submitCompletionLeaderboardName()
	{
		auto& summary = g_completionSummary;
		if (!summary.enteringName) {
			return;
		}

		if (summary.playerName.empty()) {
			summary.playerName = "PLAYER";
		}
		summary.leaderboard.push_back({
			summary.playerName,
			summary.totalScore,
			summary.completionSeconds
		});
		std::sort(
			summary.leaderboard.begin(),
			summary.leaderboard.end(),
			[](const auto& left, const auto& right) {
				if (left.score != right.score) {
					return left.score > right.score;
				}
				return left.completionSeconds < right.completionSeconds;
			});
		if (summary.leaderboard.size() > 5) {
			summary.leaderboard.resize(5);
		}
		saveLeaderboard(summary.leaderboard);
		summary.enteringName = false;
		summary.awaitingRestart = true;
	}

	std::string formatCompletionTime(double seconds)
	{
		const auto totalSeconds = (std::max)(0, static_cast<int>(std::round(seconds)));
		const auto minutes = totalSeconds / 60;
		const auto remainder = totalSeconds % 60;
		char text[32]{};
		std::snprintf(text, sizeof(text), "%02d:%02d", minutes, remainder);
		return text;
	}

	void applyCheckpointToCurrentScene(const GameCheckpoint& checkpoint) noexcept
	{
		if (!the3DEngine) {
			return;
		}

		auto& player = the3DEngine->player();
		player.setPos({ checkpoint.playerX, checkpoint.playerY });
		player.setAlpha(checkpoint.playerAlpha);
		player.setSlope(checkpoint.playerSlope);
		player.setCenterProj(checkpoint.playerCenterProj);

		g_playerCombatStats = checkpoint.combatStats;
		g_playerKeyIds = checkpoint.keyIds;
		g_minimapUnlocked = checkpoint.minimapUnlocked;
		g_minimapActorsUnlocked = checkpoint.minimapActorsUnlocked;
		g_runtimeSpriteConsumedByKey = checkpoint.runtimeSpriteConsumedByKey;
		g_runtimeSpriteExplodedByKey = checkpoint.runtimeSpriteExplodedByKey;
		g_runtimeSpriteExplosiveHealthByKey = checkpoint.runtimeSpriteExplosiveHealthByKey;
		g_runtimeActorStateByKey = checkpoint.runtimeActorStateByKey;
		syncWorldDoorKeyring();

		for (auto& info : g_runtimeSpriteInfos) {
			const auto consumed =
				g_runtimeSpriteConsumedByKey.find(info.persistenceKey);
			const auto exploded =
				g_runtimeSpriteExplodedByKey.find(info.persistenceKey);
			const auto health =
				g_runtimeSpriteExplosiveHealthByKey.find(info.persistenceKey);

			info.consumed = consumed != g_runtimeSpriteConsumedByKey.end()
				&& consumed->second;
			if (health != g_runtimeSpriteExplosiveHealthByKey.end()) {
				info.explosiveHealth = clampDouble(
					health->second,
					0.0,
					maxDouble(1.0, info.explosiveHitPoints));
			}
			info.explosionActive = false;

			if (auto* sprite = the3DEngine->sprite(info.spriteIndex)) {
				sprite->visible = !info.consumed;
			}
			if (auto* explosion = the3DEngine->sprite(info.explosionSpriteIndex)) {
				explosion->visible = false;
			}
			if (auto* destroyed = the3DEngine->sprite(info.destroyedSpriteIndex)) {
				destroyed->visible =
					exploded != g_runtimeSpriteExplodedByKey.end()
					&& exploded->second;
			}
		}

		for (auto& actor : g_spriteActors) {
			const auto state = g_runtimeActorStateByKey.find(actor.persistenceKey);
			auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (state != g_runtimeActorStateByKey.end() && sprite != nullptr) {
				applyRuntimeActorState(actor, *sprite, state->second);
			}
		}

		for (const auto& savedWeapon : checkpoint.weapons) {
			for (auto& playerWeapon : g_playerWeapons) {
				if (!playerWeaponFileMatches(playerWeapon.file, savedWeapon.file)) {
					continue;
				}

				playerWeapon.unlocked = savedWeapon.unlocked;
				if (savedWeapon.usesAmmo && playerWeapon.weapon.usesAmmo()) {
					playerWeapon.weapon.setAmmoCounts(
						savedWeapon.ammoInMagazine,
						savedWeapon.reserveAmmo);
				}
				break;
			}
		}

		size_t activeWeaponIndex = g_activePlayerWeaponIndex;
		for (size_t index = 0; index < g_playerWeapons.size(); ++index) {
			if (playerWeaponFileMatches(
				g_playerWeapons[index].file,
				checkpoint.activeWeaponFile)) {
				activeWeaponIndex = index;
				break;
			}
		}
		if (!activatePlayerWeapon(activeWeaponIndex, false)) {
			equipFirstUnlockedPlayerWeapon();
		}
	}

	void restoreAutoCheckpoint() noexcept
	{
		if (!g_autoCheckpoint.valid || !the3DEngine) {
			g_playerCombatStats.health = maxDouble(
				1.0,
				g_playerCombatStats.maxHealth * 0.35);
			g_playerLifeState = PlayerLifeState::Alive;
			g_playerDeathElapsedSeconds = 0.0;
			g_playerDeathMessageShown = false;
			return;
		}

		if (!g_autoCheckpoint.layerId.empty()
			&& g_autoCheckpoint.layerId != g_activeLayerId) {
			g_activeLayerId = g_autoCheckpoint.layerId;
			if (!SwitchToActiveLayer(nullptr, true)) {
				return;
			}
		}

		applyCheckpointToCurrentScene(g_autoCheckpoint);
		g_playerCombatStats.health = clampDouble(
			g_playerCombatStats.health,
			1.0,
			maxDouble(1.0, g_playerCombatStats.maxHealth));
		g_damageFlashSeconds = 0.0;
		g_playerLifeState = PlayerLifeState::Alive;
		g_playerDeathElapsedSeconds = 0.0;
		g_playerDeathMessageShown = false;
		g_lastActorUpdateMs = GetTickCount64();
		pushEventMessage("Recovery data restored", true);
	}

	void beginPlayerDeath() noexcept
	{
		if (g_playerImmortal || g_playerLifeState != PlayerLifeState::Alive) {
			return;
		}

		g_playerCombatStats.health = 0.0;
		g_playerLifeState = PlayerLifeState::Dying;
		g_playerDeathElapsedSeconds = 0.0;
		if (!g_playerDeathMessageShown) {
			g_playerLivesRemaining = (std::max)(0, g_playerLivesRemaining - 1);
			pushEventMessage("Energy depleted", true);
			g_playerDeathMessageShown = true;
		}
	}

	void updatePlayerLifeState(double deltaSeconds) noexcept
	{
		if (deltaSeconds <= 0.0 || g_gameCompleted || g_gameOver) {
			return;
		}

		if (g_playerLifeState == PlayerLifeState::Alive) {
			if (!g_playerImmortal) {
				g_playerCombatStats.health = maxDouble(
					0.0,
					g_playerCombatStats.health
					- kPlayerEnergyDrainPerSecond * deltaSeconds);
			}

			if (g_playerCombatStats.health <= 0.0) {
				beginPlayerDeath();
			}
			return;
		}

		g_playerDeathElapsedSeconds += deltaSeconds;
		const auto respawnDelay =
			kPlayerDeathFadeSeconds + kPlayerDeathHoldSeconds;
		if (g_playerDeathElapsedSeconds >= respawnDelay) {
			if (g_playerLivesRemaining <= 0) {
				g_gameOver = true;
				pushEventMessage("No recovery attempts remaining", true);
				return;
			}
			g_playerLifeState = PlayerLifeState::Respawning;
			restoreAutoCheckpoint();
		}
	}

	void updateGameCompletionState() noexcept
	{
		if (g_gameCompleted || !g_gameGoal.configured
			|| !the3DEngine || !theWorldMap
			|| g_activeLayerId != g_gameGoal.layerId) {
			return;
		}

		if (!g_gameGoal.requiredKey.empty()
			&& !playerHasKeyId(g_gameGoal.requiredKey)) {
			return;
		}

		const auto& player = the3DEngine->player();
		const auto playerRow = player.getRow(theWorldMap->getCellDy());
		const auto playerColumn = player.getCol(theWorldMap->getCellDx());
		if (playerRow != g_gameGoal.row
			|| playerColumn != g_gameGoal.column) {
			return;
		}

		g_gameCompleted = true;
		beginCompletionSummary();
		if (!g_gameCompletedMessageShown) {
			pushEventMessage("Mission complete", true);
			g_gameCompletedMessageShown = true;
		}
	}

	std::string essentialSaveStateSignature()
	{
		auto keys = g_playerKeyIds;
		std::sort(keys.begin(), keys.end());
		std::string signature = "keys:";
		for (const auto& key : keys) {
			signature += key + ",";
		}

		signature += g_minimapUnlocked ? "|map:1" : "|map:0";
		signature += g_minimapActorsUnlocked ? "|actors:1" : "|actors:0";
		const auto completion = currentCompletionStats();
		signature += "|kills:" + std::to_string(completion.killedEnemies);
		signature += "|weapons:";
		for (const auto& weapon : g_playerWeapons) {
			if (weapon.unlocked) {
				signature += lowerCopy(weapon.file) + ",";
			}
		}
		return signature;
	}

	void hideSavePointPanel() noexcept
	{
		g_savePointPanel.visible = false;
		g_savePointEnterWasPressed = false;
		g_savePointEscapeWasPressed = false;
	}

	void updateSavePointPanelInput()
	{
		if (!g_savePointPanel.visible) {
			return;
		}

		const bool enterPressed = pollKey(VK_RETURN);
		const bool escapePressed = pollKey(VK_ESCAPE);
		if (escapePressed && !g_savePointEscapeWasPressed) {
			hideSavePointPanel();
			pushEventMessage("Recovery cancelled");
		}
		else if (enterPressed && !g_savePointEnterWasPressed) {
			saveAutoCheckpoint(true);
			g_savedStateSignatureByPoint[g_savePointPanel.persistenceKey] =
				g_savePointPanel.stateSignature;
			hideSavePointPanel();
		}

		g_savePointEnterWasPressed = enterPressed;
		g_savePointEscapeWasPressed = escapePressed;
	}

	void updateSavePointInteraction()
	{
		if (!the3DEngine || !theWorldMap
			|| g_playerLifeState != PlayerLifeState::Alive
			|| g_gameOver || g_gameCompleted) {
			return;
		}

		const auto& player = the3DEngine->player();
		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;
		const RuntimeSpriteInfo* nearby = nullptr;
		for (const auto& info : g_runtimeSpriteInfos) {
			if (!info.savePoint || info.consumed) {
				continue;
			}

			const auto* sprite = the3DEngine->sprite(info.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - static_cast<double>(player.getX());
			const auto dy = sprite->y - static_cast<double>(player.getY());
			if (std::sqrt(dx * dx + dy * dy) <= cellSize * 0.7) {
				nearby = &info;
				break;
			}
		}

		if (nearby == nullptr) {
			g_activeSavePointPromptKey.clear();
			hideSavePointPanel();
			return;
		}

		const auto signature = essentialSaveStateSignature();
		const auto saved = g_savedStateSignatureByPoint.find(nearby->persistenceKey);
		if (saved != g_savedStateSignatureByPoint.end()
			&& saved->second == signature) {
			hideSavePointPanel();
			return;
		}

		if (nearby->persistenceKey == g_activeSavePointPromptKey
			&& g_savePointPanel.stateSignature == signature) {
			return;
		}

		g_activeSavePointPromptKey = nearby->persistenceKey;
		g_savePointPanel.visible = true;
		g_savePointPanel.persistenceKey = nearby->persistenceKey;
		g_savePointPanel.stateSignature = signature;
		pushEventMessage("Recovery station ready");
	}

	void updatePlayerPickups()
	{
		if (!the3DEngine || !theWorldMap || g_runtimeSpriteInfos.empty()) {
			return;
		}

		const auto& player = the3DEngine->player();
		const auto playerX = static_cast<double>(player.getX());
		const auto playerY = static_cast<double>(player.getY());
		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;

		const auto& log = theWorldMap->messageLog();

		for (auto& info : g_runtimeSpriteInfos) {
			if (info.consumed) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(info.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto dx = sprite->x - playerX;
			const auto dy = sprite->y - playerY;
			const auto distance = std::sqrt(dx * dx + dy * dy);
			const auto pickupRadius = maxDouble(
				cellSize * 0.32,
				maxDouble(sprite->collisionRadius, sprite->scale * 0.34));
			if (distance > pickupRadius) {
				continue;
			}

			const auto itemName = prettifyEventName(info.spriteSet);

			if (isKeyPickup(info)) {
				addPlayerKeyId(info.keyId);
				playWorldSoundEffect(kKeyPickupSoundPath);
				const auto keyName = prettifyEventName(
					info.keyId.empty() ? std::string("access") : info.keyId);
				pushEventMessage(formatEventMessage(
					log.keyPickup,
					keyName,
					std::string()), true);
				sprite->visible = false;
				info.consumed = true;
				markRuntimeSpriteConsumed(info);
				continue;
			}

			if (!info.pickupWeapon.empty()) {
				syncActivePlayerWeaponFromEngine();
				size_t weaponIndex = g_playerWeapons.size();
				const auto wasNewlyUnlocked =
					unlockPlayerWeaponByFile(info.pickupWeapon, &weaponIndex);
				if (weaponIndex < g_playerWeapons.size()) {
					auto& pickedWeapon = g_playerWeapons[weaponIndex];
					const auto weaponName = prettifyEventName(
						pickedWeapon.weapon.name().empty()
						? std::string("weapon")
						: pickedWeapon.weapon.name());
					const auto ammoRefilled = !wasNewlyUnlocked
						&& pickedWeapon.weapon.usesAmmo()
						&& pickedWeapon.weapon.refillAmmoToMax();
					if (!wasNewlyUnlocked
						&& weaponIndex == g_activePlayerWeaponIndex
						&& the3DEngine != nullptr) {
						the3DEngine->setViewWeapon(pickedWeapon.weapon);
						g_weaponAutoReloadPending = false;
					}

					playWorldSoundEffect(kAmmoPickupSoundPath);
					pushEventMessage(
						wasNewlyUnlocked
						? "Weapon acquired: " + weaponName
						: weaponName + (ammoRefilled
							? " ammunition replenished"
							: " ammunition already full"),
						wasNewlyUnlocked);
					if (wasNewlyUnlocked && !hasEquippedPlayerWeapon()) {
						activatePlayerWeapon(weaponIndex, true);
					}

					sprite->visible = false;
					info.consumed = true;
					markRuntimeSpriteConsumed(info);
				}

				continue;
			}

			if (isComputerPickup(info)) {
				if (g_minimapUnlocked) {
					g_minimapActorsUnlocked = true;
					pushEventMessage(formatEventMessage(log.mapActorsUnlocked, itemName, std::string()), true);
				}
				else {
					g_minimapUnlocked = true;
					pushEventMessage(formatEventMessage(log.mapUnlocked, itemName, std::string()), true);
				}

				playWorldSoundEffect(kComputerPickupSoundPath);
				sprite->visible = false;
				info.consumed = true;
				markRuntimeSpriteConsumed(info);
				continue;
			}

			if (isAmmoPickup(info)) {
				auto* weapon = the3DEngine->viewWeapon();
				if (weapon != nullptr
					&& weapon->usesAmmo()
					&& weapon->totalAmmo() < weapon->maxAmmo()) {
					weapon->refillAmmoToMax();
					g_weaponAutoReloadPending = false;
					playWorldSoundEffect(kAmmoPickupSoundPath);
					pushEventMessage(formatEventMessage(
						log.ammoPickup,
						itemName,
						std::to_string(weapon->totalAmmo())), false);
					sprite->visible = false;
					info.consumed = true;
					markRuntimeSpriteConsumed(info);
				}

				continue;
			}

			if (isMedikitPickup(info)
				&& g_playerCombatStats.health
				< g_playerCombatStats.maxHealth - 0.5) {
				const auto healAmount = info.pickupHealth > 0.0
					? info.pickupHealth
					: g_playerCombatStats.maxHealth * 0.35;
				g_playerCombatStats.health = clampDouble(
					g_playerCombatStats.health + healAmount,
					0.0,
					g_playerCombatStats.maxHealth);
				playWorldSoundEffect(kMedikitPickupSoundPath);
				pushEventMessage(formatEventMessage(
					log.healthPickup,
					itemName,
					formatDouble(healAmount, 0)), false);
				sprite->visible = false;
				info.consumed = true;
				markRuntimeSpriteConsumed(info);
			}
		}
	}

	bool actorHasPlayerInSight(
		const SpriteActor& actor,
		const Sprite& sprite,
		double range,
		double playerX,
		double playerY) noexcept
	{
		const auto dx = playerX - sprite.x;
		const auto dy = playerY - sprite.y;
		const auto distance = std::sqrt(dx * dx + dy * dy);
		if (distance <= 1.0 || distance > range) {
			return false;
		}

		const auto targetRadians = std::atan2(dy, dx);
		const auto halfFovRadians =
			maxDouble(1.0, actor.attackFovDegrees) * kPi / 360.0;
		if (std::abs(normalizeAngleRadians(targetRadians - sprite.facingRadians))
				> halfFovRadians) {
			return false;
		}

		return !solidWallBlocksSegment(sprite.x, sprite.y, playerX, playerY);
	}

	void applyDamageToPlayer(double damage) noexcept
	{
		if (damage <= 0.0) {
			return;
		}

		if (!g_playerImmortal) {
			g_playerCombatStats.health = maxDouble(
				0.0,
				g_playerCombatStats.health - damage);
		}

		triggerDamageFlash(damage);
		if (g_playerCombatStats.health <= 0.0) {
			beginPlayerDeath();
		}
	}

	void applyEnemyRangedShot(
		SpriteActor& shooter,
		const Sprite& shooterSprite,
		double playerX,
		double playerY,
		double cellSize) noexcept
	{
		auto hitDistance = std::sqrt(
			(playerX - shooterSprite.x) * (playerX - shooterSprite.x)
			+ (playerY - shooterSprite.y) * (playerY - shooterSprite.y));
		SpriteActor* hitActor = nullptr;
		Sprite* hitActorSprite = nullptr;
		RuntimeSpriteInfo* hitRuntimeSprite = nullptr;

		for (auto& actor : g_spriteActors) {
			if (&actor == &shooter
				|| actor.dead
				|| actor.maxHealth <= 0.0
				|| actor.health <= 0.0) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto distance = targetDistanceAlongSegment(
				shooterSprite.x,
				shooterSprite.y,
				sprite->x,
				sprite->y,
				playerX,
				playerY,
				maxDouble(sprite->collisionRadius, sprite->scale * 0.22));
			if (distance < hitDistance) {
				hitDistance = distance;
				hitActor = &actor;
				hitActorSprite = sprite;
				hitRuntimeSprite = nullptr;
			}
		}

		for (auto& info : g_runtimeSpriteInfos) {
			if (info.spriteIndex == shooter.spriteIndex
				|| !isRuntimeDamageReactive(info)
				|| info.consumed
				|| info.explosionActive) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(info.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto distance = targetDistanceAlongSegment(
				shooterSprite.x,
				shooterSprite.y,
				sprite->x,
				sprite->y,
				playerX,
				playerY,
				maxDouble(sprite->collisionRadius, sprite->scale * 0.28));
			if (distance < hitDistance) {
				hitDistance = distance;
				hitActor = nullptr;
				hitActorSprite = nullptr;
				hitRuntimeSprite = &info;
			}
		}

		const auto distanceCells = maxDouble(
			1.0,
			cellSize > 0.0 ? hitDistance / cellSize : 1.0);
		const auto damage =
			shooter.attackDamage / maxDouble(1.0, distanceCells * distanceCells);
		if (hitRuntimeSprite != nullptr) {
			damageRuntimeSprite(*hitRuntimeSprite, damage);
		}
		else if (hitActor != nullptr && hitActorSprite != nullptr) {
			hitActor->health = maxDouble(0.0, hitActor->health - damage);
			if (hitActor->health <= 0.0) {
				destroyActor(*hitActor, *hitActorSprite);
			}
			else {
				storeRuntimeActorState(*hitActor);
			}
		}
		else {
			applyDamageToPlayer(damage);
		}
	}

	void updateActorMeleeAttacks() noexcept
	{
		if (!the3DEngine || !theWorldMap) {
			return;
		}

		const auto& player = the3DEngine->player();
		const auto playerX = static_cast<double>(player.getX());
		const auto playerY = static_cast<double>(player.getY());
		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;

		for (auto& actor : g_spriteActors) {
			if (actor.rangedAttack
				|| actor.attackDamage <= 0.0
				|| actor.attackCooldownRemaining > 0.0
				|| actor.dead
				|| (actor.maxHealth > 0.0 && actor.health <= 0.0)) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto rangeCells = maxDouble(0.35, actor.stoppingDistanceCells + 0.1);
			const auto range = rangeCells * cellSize;
			if (range <= 0.0
				|| !actorHasPlayerInSight(actor, *sprite, range, playerX, playerY)) {
				continue;
			}

			sprite->setAnimationOrFallback("attack", "idle");
			playWorldSoundEffect(kEnemyMeleeAttackSoundPath, 75);
			applyDamageToPlayer(actor.attackDamage);
			actor.attackCooldownRemaining =
				maxDouble(0.1, actor.attackCooldownSeconds);
		}
	}

	void updateActorRangedAttacks(double deltaSeconds) noexcept
	{
		if (!the3DEngine || !theWorldMap || deltaSeconds <= 0.0) {
			return;
		}

		const auto& player = the3DEngine->player();
		const auto playerX = static_cast<double>(player.getX());
		const auto playerY = static_cast<double>(player.getY());
		const auto cellSize =
			(static_cast<double>(theWorldMap->getCellDx())
				+ static_cast<double>(theWorldMap->getCellDy())) * 0.5;

		for (auto& actor : g_spriteActors) {
			if (!actor.rangedAttack
				|| actor.attackDamage <= 0.0
				|| actor.dead
				|| (actor.maxHealth > 0.0 && actor.health <= 0.0)) {
				continue;
			}

			auto* sprite = the3DEngine->sprite(actor.spriteIndex);
			if (sprite == nullptr || !sprite->visible) {
				continue;
			}

			const auto rangeCells = actor.attackRangeCells > 0.0
				? actor.attackRangeCells
				: actor.detectionRadiusCells;
			const auto range = maxDouble(0.0, rangeCells) * cellSize;
			if (range <= 0.0
				|| !actorHasPlayerInSight(actor, *sprite, range, playerX, playerY)) {
				actor.attackBurstShotsRemaining = 0;
				actor.attackHoldSecondsRemaining = 0.0;
				continue;
			}

			if (actor.attackCooldownRemaining > 0.0) {
				continue;
			}

			const auto startingBurst = actor.attackBurstShotsRemaining <= 0;
			if (startingBurst) {
				actor.attackBurstShotsRemaining =
					(std::max)(1, actor.attackBurstShots);
			}

			const auto dx = playerX - sprite->x;
			const auto dy = playerY - sprite->y;
			sprite->facingRadians = std::atan2(dy, dx);
			if (sprite->facingRadians < 0.0) {
				sprite->facingRadians += kPi * 2.0;
			}

			sprite->setAnimationOrFallback("attack", "idle");
			playWorldSoundEffect(kEnemyRangedAttackSoundPath, 70);

			applyEnemyRangedShot(actor, *sprite, playerX, playerY, cellSize);

			--actor.attackBurstShotsRemaining;
			actor.attackHoldSecondsRemaining =
				clampDouble(actor.attackCooldownSeconds * 0.45, 0.12, 0.35);
			actor.attackCooldownRemaining =
				actor.attackBurstShotsRemaining > 0
				? maxDouble(0.1, actor.attackCooldownSeconds)
				: maxDouble(0.1, actor.attackBurstPauseSeconds);
		}
	}

	void fillRect(HDC hdc, const RECT& rect, COLORREF color) noexcept
	{
		BrushHandle brush(CreateSolidBrush(color));
		if (brush) {
			FillRect(hdc, &rect, brush.get());
		}
	}

	void drawTextLine(
		HDC hdc,
		int x,
		int y,
		const std::string& text,
		COLORREF color) noexcept
	{
		SetBkMode(hdc, TRANSPARENT);
		SetTextColor(hdc, color);
		TextOutA(hdc, x, y, text.c_str(), static_cast<int>(text.size()));
	}

	std::string formatDouble(double value, int decimals)
	{
		char buffer[64] = { 0 };
		std::snprintf(buffer, sizeof(buffer), "%.*f", decimals, value);
		return buffer;
	}

	int cameraFacingDegrees(const Player& player) noexcept
	{
		const auto ray = normalizeRay(
			player.getAlpha() + player.degHalfVisual(),
			player);
		return static_cast<int>(
			std::round(
				static_cast<double>(ray) * 360.0
				/ static_cast<double>(player.deg360()))) % 360;
	}

	bool cellLooksBlocking(int row, int column, Cell cell) noexcept
	{
		if (!theWorldMap) {
			return false;
		}

		const auto* block = theWorldMap->blockAtCell(row, column);
		if (block != nullptr) {
			return block->hasAnyCollidingSpan || block->hasAnySolidSpan;
		}

		return MapCell::hasSolidWall(cell);
	}

	bool cellLooksTransparent(int row, int column, Cell cell) noexcept
	{
		if (!theWorldMap) {
			return false;
		}

		const auto* block = theWorldMap->blockAtCell(row, column);
		if (block != nullptr) {
			return block->hasAnyTransparentSpan;
		}

		return MapCell::hasTransparentWall(cell);
	}

	bool isLayerTransitionCell(int row, int column) noexcept
	{
		if (!theWorldMap) {
			return false;
		}

		const auto blockId = theWorldMap->blockIdAt(row, column);
		for (const auto& transition : g_layerTransitions) {
			if (!g_activeLayerId.empty()
				&& transition.fromLayer != g_activeLayerId) {
				continue;
			}

			if (transition.hasTriggerCell
				&& (transition.triggerRow != row
					|| transition.triggerColumn != column)) {
				continue;
			}

			uint8_t triggerBlockId = 0;
			if (tryParseBlockId(transition.triggerBlockId, triggerBlockId)
				&& triggerBlockId == blockId) {
				return true;
			}
		}

		return false;
	}

	void drawMinimapDoorMarker(
		HDC hdc,
		const RECT& cellRect,
		double openAmount,
		const std::string& requiredKey)
	{
		const auto width = static_cast<int>(cellRect.right - cellRect.left);
		const auto height = static_cast<int>(cellRect.bottom - cellRect.top);
		const auto minSize = (std::min)(width, height);
		if (minSize < 3) {
			return;
		}

		const auto inset = minSize >= 6 ? 1 : 0;
		const auto color = !requiredKey.empty()
			? colorForKeyId(requiredKey)
			: (openAmount >= 0.75
				? RGB(80, 212, 150)
				: (openAmount > 0.02
					? RGB(245, 190, 70)
					: RGB(236, 112, 54)));
		RECT marker{
			cellRect.left + inset,
			cellRect.top + inset,
			cellRect.right - inset,
			cellRect.bottom - inset
		};
		fillRect(hdc, marker, color);

		if (requiredKey.empty() || minSize < 8) {
			return;
		}

		const auto cx = (cellRect.left + cellRect.right) / 2;
		const auto cy = (cellRect.top + cellRect.bottom) / 2;
		const auto keyRadius = (std::max)(2, minSize / 6);
		PenHandle keyPen(CreatePen(PS_SOLID, (std::max)(1, minSize / 9), RGB(24, 24, 20)));
		if (!keyPen) {
			return;
		}

		SelectObjectScope penSelect(hdc, keyPen.get());
		SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
		Ellipse(
			hdc,
			cx - keyRadius - 2,
			cy - keyRadius,
			cx + keyRadius - 1,
			cy + keyRadius + 1);
		MoveToEx(hdc, cx + keyRadius - 1, cy, nullptr);
		LineTo(hdc, cx + keyRadius + 4, cy);
		LineTo(hdc, cx + keyRadius + 4, cy + 3);
	}

	void drawMinimapElevatorMarker(
		HDC hdc,
		const RECT& cellRect) noexcept
	{
		const auto width = static_cast<int>(cellRect.right - cellRect.left);
		const auto height = static_cast<int>(cellRect.bottom - cellRect.top);
		const auto minSize = (std::min)(width, height);
		if (minSize < 5) {
			return;
		}

		const auto radius = (std::max)(2, minSize / 3);
		const auto cx = (cellRect.left + cellRect.right) / 2;
		const auto cy = (cellRect.top + cellRect.bottom) / 2;
		BrushHandle liftBrush(CreateSolidBrush(RGB(120, 86, 214)));
		PenHandle liftPen(CreatePen(PS_SOLID, 1, RGB(245, 225, 120)));
		if (liftBrush && liftPen) {
			SelectObjectScope brushSelect(hdc, liftBrush.get());
			SelectObjectScope penSelect(hdc, liftPen.get());
			Ellipse(
				hdc,
				cx - radius,
				cy - radius,
				cx + radius + 1,
				cy + radius + 1);
		}

		if (minSize >= 13) {
			drawTextLine(hdc, cx - 4, cy - 7, "L", RGB(255, 245, 180));
		}
	}

	void drawRuntimeMinimap(
		HDC hdc,
		int left,
		int top,
		int size) noexcept
	{
		if (!theWorldMap || !the3DEngine) {
			return;
		}

		const auto rows = theWorldMap->getRowCount();
		const auto cols = theWorldMap->getColCount();
		if (rows <= 0 || cols <= 0) {
			return;
		}

		RECT frame{ left, top, left + size, top + size };
		fillRect(hdc, frame, RGB(10, 12, 14));

		const auto cellSize = maxDouble(
			1.0,
			(std::min)(
				static_cast<double>(size) / static_cast<double>(cols),
				static_cast<double>(size) / static_cast<double>(rows)));
		const auto mapWidth = static_cast<int>(std::round(cellSize * cols));
		const auto mapHeight = static_cast<int>(std::round(cellSize * rows));
		const auto mapLeft = left + (size - mapWidth) / 2;
		const auto mapTop = top + (size - mapHeight) / 2;

		for (int row = 0; row < rows; ++row) {
			for (int col = 0; col < cols; ++col) {
				const auto cell = (*theWorldMap)[static_cast<uint32_t>(row)]
					[static_cast<uint32_t>(col)];
				const auto x0 = mapLeft + static_cast<int>(std::floor(col * cellSize));
				const auto y0 = mapTop + static_cast<int>(std::floor(row * cellSize));
				const auto x1 = mapLeft + static_cast<int>(std::ceil((col + 1) * cellSize));
				const auto y1 = mapTop + static_cast<int>(std::ceil((row + 1) * cellSize));

				const auto color = cellLooksBlocking(row, col, cell)
					? RGB(64, 64, 64)
					: (cellLooksTransparent(row, col, cell)
						? RGB(155, 180, 188)
						: RGB(178, 188, 172));

				RECT cellRect{ x0, y0, x1, y1 };
				fillRect(hdc, cellRect, color);

				const auto* block = theWorldMap->blockAtCell(row, col);
				if (block != nullptr && block->door.enabled) {
					drawMinimapDoorMarker(
						hdc,
						cellRect,
						theWorldMap->doorOpenAmountAt(row, col),
						block->door.requiredKey);
				}

				if (isLayerTransitionCell(row, col)) {
					drawMinimapElevatorMarker(hdc, cellRect);
				}
			}
		}

		PenHandle borderPen(CreatePen(PS_SOLID, 1, RGB(230, 210, 150)));
		if (borderPen) {
			SelectObjectScope borderSelect(hdc, borderPen.get());
			SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
			Rectangle(hdc, left, top, left + size, top + size);
		}

		drawTextLine(hdc, left + size / 2 - 4, top + 3, "N", RGB(255, 225, 120));
		drawTextLine(hdc, left + size / 2 - 4, top + size - 15, "S", RGB(255, 225, 120));
		drawTextLine(hdc, left + 4, top + size / 2 - 7, "W", RGB(255, 225, 120));
		drawTextLine(hdc, left + size - 12, top + size / 2 - 7, "E", RGB(255, 225, 120));

		const auto& player = the3DEngine->player();
		const auto worldToMapX = [=](double x) {
			return mapLeft
				+ static_cast<int>(std::round(
					x / maxDouble(1.0, static_cast<double>(theWorldMap->getMaxX()))
					* static_cast<double>(mapWidth)));
			};
		const auto worldToMapY = [=](double y) {
			return mapTop
				+ static_cast<int>(std::round(
					y / maxDouble(1.0, static_cast<double>(theWorldMap->getMaxY()))
					* static_cast<double>(mapHeight)));
			};

		if (g_minimapActorsUnlocked) {
			BrushHandle actorBrush(CreateSolidBrush(RGB(255, 118, 36)));
			PenHandle actorPen(CreatePen(PS_SOLID, 1, RGB(40, 8, 0)));
			if (actorBrush && actorPen) {
				SelectObjectScope brushSelect(hdc, actorBrush.get());
				SelectObjectScope penSelect(hdc, actorPen.get());
				for (const auto& actor : g_spriteActors) {
					if (actor.dead
						|| (actor.maxHealth > 0.0 && actor.health <= 0.0)) {
						continue;
					}

					const auto* sprite = the3DEngine->sprite(actor.spriteIndex);
					if (sprite == nullptr || !sprite->visible) {
						continue;
					}

					const auto ax = worldToMapX(sprite->x);
					const auto ay = worldToMapY(sprite->y);
					Ellipse(hdc, ax - 3, ay - 3, ax + 4, ay + 4);
				}
			}

			for (const auto& info : g_runtimeSpriteInfos) {
				if (info.consumed || !isKeyPickup(info)) {
					continue;
				}

				const auto* sprite = the3DEngine->sprite(info.spriteIndex);
				if (sprite == nullptr || !sprite->visible) {
					continue;
				}

				const auto keyColor = colorForKeyId(info.keyId);
				BrushHandle keyBrush(CreateSolidBrush(keyColor));
				PenHandle keyPen(CreatePen(PS_SOLID, 1, RGB(25, 25, 20)));
				if (!keyBrush || !keyPen) {
					continue;
				}

				const auto kx = worldToMapX(sprite->x);
				const auto ky = worldToMapY(sprite->y);
				POINT diamond[] = {
					{ kx, ky - 5 },
					{ kx + 5, ky },
					{ kx, ky + 5 },
					{ kx - 5, ky }
				};
				SelectObjectScope brushSelect(hdc, keyBrush.get());
				SelectObjectScope penSelect(hdc, keyPen.get());
				Polygon(hdc, diamond, 4);
			}
		}

		const auto px = worldToMapX(static_cast<double>(player.getX()));
		const auto py = worldToMapY(static_cast<double>(player.getY()));

		BrushHandle playerBrush(CreateSolidBrush(RGB(240, 48, 48)));
		if (playerBrush) {
			SelectObjectScope brushSelect(hdc, playerBrush.get());
			Ellipse(hdc, px - 4, py - 4, px + 5, py + 5);
		}

		const auto headingRadians = rayToRadians(
			player,
			normalizeRay(player.getAlpha() + player.degHalfVisual(), player));
		PenHandle headingPen(CreatePen(PS_SOLID, 2, RGB(255, 235, 80)));
		if (headingPen) {
			SelectObjectScope penSelect(hdc, headingPen.get());
			MoveToEx(hdc, px, py, nullptr);
			LineTo(
				hdc,
				px + static_cast<int>(std::round(std::cos(headingRadians) * 15.0)),
				py + static_cast<int>(std::round(std::sin(headingRadians) * 15.0)));
		}
	}

	void drawHudIconSlot(
		HDC hdc,
		const RECT& slot,
		COLORREF borderColor,
		bool emphasized) noexcept
	{
		fillRect(hdc, slot, RGB(18, 21, 26));

		PenHandle borderPen(CreatePen(PS_SOLID, emphasized ? 2 : 1, borderColor));
		if (borderPen) {
			SelectObjectScope penSelect(hdc, borderPen.get());
			SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
			Rectangle(hdc, slot.left, slot.top, slot.right, slot.bottom);
		}
	}

	void drawHudTextureIcon(
		HDC hdc,
		const Texture& texture,
		const RECT& slot) noexcept
	{
		RECT imageBounds{
			slot.left + 4,
			slot.top + 4,
			slot.right - 4,
			slot.bottom - 4
		};
		const auto fitted = fitTextureRect(texture, imageBounds);
		drawTextureAlphaOnSolidBackground(hdc, texture, fitted, RGB(18, 21, 26));
	}

	void drawHudKeyFallbackIcon(
		HDC hdc,
		const RECT& slot,
		COLORREF color) noexcept
	{
		const auto slotHeight = static_cast<int>(slot.bottom - slot.top);
		const auto centerY = slot.top + slotHeight / 2;
		const auto ringSize = (std::min)(10, slotHeight / 3);
		RECT ring{
			slot.left + 7,
			centerY - ringSize / 2,
			slot.left + 7 + ringSize,
			centerY + ringSize / 2
		};

		BrushHandle brush(CreateSolidBrush(color));
		PenHandle pen(CreatePen(PS_SOLID, 2, RGB(20, 20, 18)));
		if (!brush || !pen) {
			return;
		}

		SelectObjectScope brushSelect(hdc, brush.get());
		SelectObjectScope penSelect(hdc, pen.get());
		Ellipse(hdc, ring.left, ring.top, ring.right, ring.bottom);
		MoveToEx(hdc, ring.right, centerY, nullptr);
		LineTo(hdc, slot.right - 8, centerY);
		LineTo(hdc, slot.right - 8, centerY + 6);
		MoveToEx(hdc, slot.right - 13, centerY, nullptr);
		LineTo(hdc, slot.right - 13, centerY + 5);
	}

	void drawHudInventoryIcons(
		HDC hdc,
		int contentLeft,
		int& cursorY,
		int contentWidth) noexcept
	{
		if (!hdc || contentWidth <= 44) {
			return;
		}

		const auto muted = RGB(126, 132, 142);
		const auto label = RGB(255, 220, 88);
		const auto normalBorder = RGB(76, 84, 96);
		const auto activeBorder = RGB(255, 205, 82);

		drawTextLine(hdc, contentLeft, cursorY, "KEYS", label);
		if (g_playerKeyIds.empty()) {
			drawTextLine(hdc, contentLeft + 48, cursorY, "none", muted);
			cursorY += 24;
		}
		else {
			const auto slotSize = 34;
			const auto gap = 6;
			auto x = contentLeft;
			auto y = cursorY + 18;
			for (const auto& keyId : g_playerKeyIds) {
				if (x != contentLeft && x + slotSize > contentLeft + contentWidth) {
					x = contentLeft;
					y += slotSize + gap;
				}

				RECT slot{ x, y, x + slotSize, y + slotSize };
				drawHudIconSlot(hdc, slot, colorForKeyId(keyId), false);
				if (const auto* texture = keyHudTextureForId(keyId)) {
					drawHudTextureIcon(hdc, *texture, slot);
				}
				else {
					drawHudKeyFallbackIcon(hdc, slot, colorForKeyId(keyId));
				}
				x += slotSize + gap;
			}
			cursorY = y + slotSize + 12;
		}

		drawTextLine(hdc, contentLeft, cursorY, "WEAPONS", label);
		const auto weaponSlotWidth = 58;
		const auto weaponSlotHeight = 42;
		const auto weaponGap = 7;
		auto weaponX = contentLeft;
		auto weaponY = cursorY + 18;
		auto visibleWeapons = 0;

		for (size_t index = 0; index < g_playerWeapons.size(); ++index) {
			const auto& runtimeWeapon = g_playerWeapons[index];
			if (!runtimeWeapon.unlocked) {
				continue;
			}

			if (weaponX != contentLeft
				&& weaponX + weaponSlotWidth > contentLeft + contentWidth) {
				weaponX = contentLeft;
				weaponY += weaponSlotHeight + weaponGap;
			}

			const auto active = index == g_activePlayerWeaponIndex;
			RECT slot{
				weaponX,
				weaponY,
				weaponX + weaponSlotWidth,
				weaponY + weaponSlotHeight
			};
			drawHudIconSlot(
				hdc,
				slot,
				active ? activeBorder : normalBorder,
				active);

			if (const auto* texture = runtimeWeapon.weapon.currentFrame()) {
				drawHudTextureIcon(hdc, *texture, slot);
			}

			drawTextLine(
				hdc,
				slot.left + 4,
				slot.top + 3,
				std::to_string(index + 1),
				active ? RGB(255, 232, 130) : RGB(170, 176, 186));

			weaponX += weaponSlotWidth + weaponGap;
			++visibleWeapons;
		}

		if (visibleWeapons == 0) {
			drawTextLine(hdc, contentLeft + 72, cursorY, "none", muted);
			cursorY += 24;
		}
		else {
			cursorY = weaponY + weaponSlotHeight + 16;
		}
	}

	std::string elevatorLayerDisplayName(const std::string& layerId)
	{
		const auto it = g_layerDisplayNames.find(layerId);
		if (it != g_layerDisplayNames.end() && !it->second.empty()) {
			return it->second;
		}

		return "Uncharted destination";
	}

	void drawElevatorSelectionPanel(
		HDC hdc,
		const RECT& viewport) noexcept
	{
		if (!hdc || !g_elevatorPanel.visible
			|| g_elevatorPanel.transitionIndices.empty()
			|| viewport.right - viewport.left < 240
			|| viewport.bottom - viewport.top < 180) {
			return;
		}

		static FontHandle promptFont(CreateFontA(
			-22, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
			ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
			CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, "Segoe UI"));
		SelectObjectScope promptFontSelect(hdc, promptFont.get());
		const auto viewportWidth = static_cast<int>(viewport.right - viewport.left);
		const auto viewportHeight = static_cast<int>(viewport.bottom - viewport.top);
		const auto panelWidth = (std::min)(560, static_cast<int>(viewportWidth - 48));
		const auto panelHeight = 116
			+ static_cast<int>(g_elevatorPanel.transitionIndices.size()) * 34;
		RECT panel{
			viewport.left + (viewportWidth - panelWidth) / 2,
			viewport.top + (viewportHeight - panelHeight) / 2,
			viewport.left + (viewportWidth + panelWidth) / 2,
			viewport.top + (viewportHeight + panelHeight) / 2
		};
		fillRect(hdc, panel, RGB(18, 22, 30));
		PenHandle borderPen(CreatePen(PS_SOLID, 1, RGB(120, 86, 214)));
		if (borderPen) {
			SelectObjectScope penSelect(hdc, borderPen.get());
			SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
			Rectangle(hdc, panel.left, panel.top, panel.right, panel.bottom);
		}

		auto y = panel.top + 16;
		drawTextLine(hdc, panel.left + 18, y, "ELEVATOR", RGB(255, 220, 88));
		y += 30;
		drawTextLine(hdc, panel.left + 18, y, "Choose destination", RGB(200, 210, 220));
		y += 40;

		for (size_t option = 0; option < g_elevatorPanel.transitionIndices.size(); ++option) {
			const auto transitionIndex = g_elevatorPanel.transitionIndices[option];
			if (transitionIndex >= g_layerTransitions.size()) {
				continue;
			}

			const auto selected = option == g_elevatorPanel.selectedIndex;
			const auto& transition = g_layerTransitions[transitionIndex];
			const auto locked = !transition.requiredKey.empty()
				&& !playerHasKeyId(transition.requiredKey);
			if (selected) {
				RECT selection{
					panel.left + 10,
					y - 4,
					panel.right - 10,
					y + 26
				};
				fillRect(hdc, selection, RGB(54, 43, 92));
			}

			const auto keyLabel = transition.requiredKey.empty()
				? std::string()
				: " [" + transition.requiredKey + " key]";
			const auto label = std::to_string(option + 1) + ". "
				+ elevatorLayerDisplayName(transition.toLayer)
				+ keyLabel;
			drawTextLine(
				hdc,
				panel.left + 20,
				y,
				(selected ? "> " : "  ") + label,
				locked
					? RGB(235, 105, 90)
					: (selected ? RGB(255, 235, 125) : RGB(230, 230, 230)));
			y += 34;
		}

		drawTextLine(
			hdc,
			panel.left + 18,
			panel.bottom - 28,
			"Arrows: select   Enter: go   Esc: cancel",
			RGB(130, 138, 150));
	}

	void drawSavePointPanel(
		HDC hdc,
		const RECT& viewport) noexcept
	{
		if (!hdc || !g_savePointPanel.visible
			|| viewport.right - viewport.left < 240
			|| viewport.bottom - viewport.top < 160) {
			return;
		}

		static FontHandle promptFont(CreateFontA(
			-22, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
			ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
			CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, "Segoe UI"));
		SelectObjectScope promptFontSelect(hdc, promptFont.get());
		const auto viewportWidth = viewport.right - viewport.left;
		const auto viewportHeight = viewport.bottom - viewport.top;
		const auto panelWidth = (std::min)(520, static_cast<int>(viewportWidth - 48));
		const auto panelHeight = 150;
		RECT panel{
			viewport.left + (viewportWidth - panelWidth) / 2,
			viewport.top + (viewportHeight - panelHeight) / 2,
			viewport.left + (viewportWidth + panelWidth) / 2,
			viewport.top + (viewportHeight + panelHeight) / 2
		};
		fillRect(hdc, panel, RGB(18, 22, 30));
		PenHandle borderPen(CreatePen(PS_SOLID, 1, RGB(55, 156, 118)));
		if (borderPen) {
			SelectObjectScope penSelect(hdc, borderPen.get());
			SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
			Rectangle(hdc, panel.left, panel.top, panel.right, panel.bottom);
		}

		drawTextLine(hdc, panel.left + 18, panel.top + 18, "RECOVERY STATION", RGB(255, 220, 88));
		drawTextLine(hdc, panel.left + 18, panel.top + 58, "Secure recovery data?", RGB(230, 230, 230));
		drawTextLine(hdc, panel.left + 18, panel.bottom - 32, "Enter: confirm   Esc: cancel", RGB(150, 158, 170));
	}

	void drawCompletionSummaryPanel(HDC hdc, const RECT& viewport) noexcept
	{
		const auto& summary = g_completionSummary;
		const auto viewportWidth = static_cast<int>(viewport.right - viewport.left);
		const auto viewportHeight = static_cast<int>(viewport.bottom - viewport.top);
		if (!hdc || !summary.active || viewportWidth < 360 || viewportHeight < 360) {
			return;
		}

		static FontHandle titleFont(CreateFontA(
			-30, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
			ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
			CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, "Segoe UI"));
		static FontHandle bodyFont(CreateFontA(
			-19, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
			ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
			CLEARTYPE_QUALITY, FIXED_PITCH | FF_MODERN, "Consolas"));
		const auto panelWidth = (std::min)(680, viewportWidth - 40);
		const auto desiredHeight = summary.countingComplete ? 650 : 430;
		const auto panelHeight = (std::min)(desiredHeight, viewportHeight - 30);
		RECT panel{
			viewport.left + (viewportWidth - panelWidth) / 2,
			viewport.top + (viewportHeight - panelHeight) / 2,
			viewport.left + (viewportWidth + panelWidth) / 2,
			viewport.top + (viewportHeight + panelHeight) / 2
		};
		fillRect(hdc, panel, RGB(12, 16, 22));
		PenHandle borderPen(CreatePen(PS_SOLID, 2, RGB(205, 162, 58)));
		if (borderPen) {
			SelectObjectScope penSelect(hdc, borderPen.get());
			SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
			Rectangle(hdc, panel.left, panel.top, panel.right, panel.bottom);
		}

		{
			SelectObjectScope titleSelect(hdc, titleFont.get());
			const std::string title = "MISSION COMPLETE";
			SIZE titleSize{};
			GetTextExtentPoint32A(hdc, title.c_str(), static_cast<int>(title.size()), &titleSize);
			drawTextLine(hdc, panel.left + (panelWidth - titleSize.cx) / 2,
				panel.top + 18, title, RGB(255, 220, 88));
		}

		SelectObjectScope bodySelect(hdc, bodyFont.get());
		auto y = panel.top + 68;
		const auto x = panel.left + 34;
		auto line = [&](const std::string& label, COLORREF color = RGB(225, 230, 235)) {
			drawTextLine(hdc, x, y, label, color);
			y += 28;
		};
		line("TIME                 " + formatCompletionTime(summary.completionSeconds));
		line("ENEMIES DEFEATED     " + std::to_string(summary.displayedEnemies)
			+ "/" + std::to_string(summary.stats.totalEnemies)
			+ "   +" + std::to_string(summary.displayedEnemies * 500));
		line("ITEMS                " + std::to_string(summary.displayedItems)
			+ "/" + std::to_string(summary.stats.totalItems)
			+ "   +" + std::to_string(summary.displayedItems * 100));
		line("PROPERTY DESTROYED   " + std::to_string(summary.displayedDestroyedProps)
			+ "/" + std::to_string(summary.stats.totalDestructibleProps)
			+ "   -" + std::to_string(summary.displayedDestroyedProps * 150),
			RGB(240, 155, 130));
		line("TIME BONUS                   +" + std::to_string(summary.timeBonus));
		line("MISSION BONUS                +5000");
		line("TOTAL SCORE                  " + std::to_string(summary.displayedScore), RGB(255, 220, 88));

		if (!summary.countingComplete) {
			return;
		}

		y += 8;
		line("TOP FIVE", RGB(140, 205, 255));
		if (summary.leaderboard.empty()) {
			line("No completed missions recorded", RGB(140, 148, 160));
		}
		else {
			for (size_t index = 0; index < summary.leaderboard.size(); ++index) {
				const auto& entry = summary.leaderboard[index];
				line(std::to_string(index + 1) + ". " + entry.name
					+ "   " + std::to_string(entry.score)
					+ "   " + formatCompletionTime(entry.completionSeconds));
			}
		}

		if (summary.enteringName) {
			line("TOP FIVE - ENTER NAME: " + summary.playerName + "_", RGB(150, 230, 150));
			line("ENTER: SAVE   ESC: USE PLAYER", RGB(150, 158, 170));
		}
		else if (summary.awaitingRestart) {
			line("START A NEW MISSION?   Y: YES   N: NO", RGB(150, 230, 150));
		}
	}

	void drawRuntimeHud(
		HDC hdc,
		int videoPosX,
		int videoPosY,
		int clientWidth,
		int clientHeight,
		int renderWidth,
		int renderHeight) noexcept
	{
		if (!hdc
			|| !the3DEngine
			|| !theWorldMap
			|| clientWidth <= 0
			|| clientHeight <= 0
			|| renderWidth <= 0
			|| renderHeight <= 0) {
			return;
		}

		// The HUD font never changes; create it once and reuse it. Recreating a
		// GDI font every frame (font mapping) is pure overhead on the per-frame
		// HUD path. UI is single-threaded, so a static handle is safe.
		static FontHandle font(CreateFontA(
			-13,
			0,
			0,
			0,
			FW_SEMIBOLD,
			FALSE,
			FALSE,
			FALSE,
			ANSI_CHARSET,
			OUT_DEFAULT_PRECIS,
			CLIP_DEFAULT_PRECIS,
			DEFAULT_QUALITY,
			DEFAULT_PITCH | FF_DONTCARE,
			"Consolas"));
		SelectObjectScope fontSelect(hdc, font.get());

		RECT viewport{
			videoPosX,
			videoPosY,
			videoPosX + renderWidth,
			videoPosY + renderHeight
		};

		PenHandle viewportPen(CreatePen(PS_SOLID, 1, RGB(95, 95, 95)));
		if (viewportPen) {
			SelectObjectScope penSelect(hdc, viewportPen.get());
			SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
			Rectangle(hdc, viewport.left, viewport.top, viewport.right, viewport.bottom);
		}

		if (g_gameCompleted) {
			drawCompletionSummaryPanel(hdc, viewport);
		}
		else if (g_gameOver || g_playerLifeState != PlayerLifeState::Alive) {
			const auto message = g_gameOver
				? std::string("GAME OVER - ENTER TO RESTART")
				: std::string("RESTORING RECOVERY DATA");
			SIZE messageSize{};
			GetTextExtentPoint32A(
				hdc,
				message.c_str(),
				static_cast<int>(message.size()),
				&messageSize);
			drawTextLine(
				hdc,
				viewport.left + (renderWidth - messageSize.cx) / 2,
				viewport.top + renderHeight / 2 - 8,
				message,
				RGB(255, 210, 210));
		}

		const auto margin = 14;
		RECT rightPanel{
			videoPosX + renderWidth,
			videoPosY,
			videoPosX + clientWidth,
			videoPosY + clientHeight
		};
		if (rightPanel.right > rightPanel.left) {
			fillRect(hdc, rightPanel, RGB(10, 12, 15));
		}

		// Bottom teletype: a short rolling event log beneath the projection. The panel
		// also covers any aspect-ratio letterbox so no stale pixels show.
		RECT bottomPanel{
			videoPosX,
			videoPosY + renderHeight,
			videoPosX + renderWidth,
			videoPosY + clientHeight
		};
		if (bottomPanel.bottom > bottomPanel.top) {
			fillRect(hdc, bottomPanel, RGB(13, 15, 18));

			PenHandle topSeparator(CreatePen(PS_SOLID, 1, RGB(55, 58, 64)));
			if (topSeparator) {
				SelectObjectScope penSelect(hdc, topSeparator.get());
				MoveToEx(hdc, bottomPanel.left, bottomPanel.top, nullptr);
				LineTo(hdc, bottomPanel.right, bottomPanel.top);
			}

			if (theWorldMap->messageLog().enabled && !g_eventLog.empty()) {
				const auto maxLines = clampInt(theWorldMap->messageLog().maxLines, 1, 4);
				const size_t firstLine = g_eventLog.size() > static_cast<size_t>(maxLines)
					? g_eventLog.size() - static_cast<size_t>(maxLines)
					: 0;
				int lineY = bottomPanel.top + 8;
				for (size_t i = firstLine; i < g_eventLog.size(); ++i) {
					// Newest line brightest, older lines progressively dimmer (teletype feel).
					const auto fromNewest = static_cast<int>(g_eventLog.size() - 1 - i);
					const BYTE shade = static_cast<BYTE>((std::max)(120, 235 - fromNewest * 32));
					drawTextLine(
						hdc,
						bottomPanel.left + margin,
						lineY,
						"> " + g_eventLog[i].text,
						RGB(shade, shade, shade));
					lineY += EVENT_LOG_LINE_HEIGHT;
				}
			}
		}

		PenHandle separatorPen(CreatePen(PS_SOLID, 1, RGB(55, 58, 64)));
		if (separatorPen && rightPanel.right > rightPanel.left) {
			SelectObjectScope penSelect(hdc, separatorPen.get());
			MoveToEx(hdc, rightPanel.left, videoPosY, nullptr);
			LineTo(hdc, rightPanel.left, videoPosY + clientHeight);
		}

		const auto rightPanelWidth =
			static_cast<int>(rightPanel.right - rightPanel.left);
		const auto minimapSize = (std::min)(
			(std::max)(84, rightPanelWidth - margin * 2),
			(std::max)(84, (std::min)(renderHeight / 2, 356)));
		const auto minimapTop = rightPanel.top + margin;
		if (rightPanelWidth >= minimapSize + margin * 2) {
			const auto minimapLeft = rightPanel.left + margin;
			if (g_minimapUnlocked) {
				drawTextLine(
					hdc,
					minimapLeft,
					minimapTop + minimapSize + 10,
					"MAP",
					RGB(255, 220, 88));
				drawRuntimeMinimap(hdc, minimapLeft, minimapTop, minimapSize);
				drawTextLine(
					hdc,
					minimapLeft + 42,
					minimapTop + minimapSize + 10,
					"DOORS",
					RGB(236, 112, 54));
				drawTextLine(
					hdc,
					minimapLeft + 104,
					minimapTop + minimapSize + 10,
					"LIFTS",
					RGB(160, 130, 245));
				if (g_minimapActorsUnlocked) {
					drawTextLine(
						hdc,
						minimapLeft + 164,
						minimapTop + minimapSize + 10,
						"HOSTILES",
						RGB(255, 145, 72));
					drawTextLine(
						hdc,
						minimapLeft + 250,
						minimapTop + minimapSize + 10,
						"KEYS",
						RGB(250, 218, 74));
				}
			}
			else {
				RECT lockedMap{ minimapLeft, minimapTop, minimapLeft + minimapSize, minimapTop + minimapSize };
				fillRect(hdc, lockedMap, RGB(10, 12, 14));
				PenHandle lockedPen(CreatePen(PS_SOLID, 1, RGB(82, 88, 96)));
				if (lockedPen) {
					SelectObjectScope penSelect(hdc, lockedPen.get());
					SelectObjectScope nullBrushSelect(hdc, GetStockObject(NULL_BRUSH));
					Rectangle(hdc, lockedMap.left, lockedMap.top, lockedMap.right, lockedMap.bottom);
				}

				drawTextLine(
					hdc,
					minimapLeft + 12,
					minimapTop + minimapSize / 2 - 8,
					"MAP OFFLINE",
					RGB(125, 132, 142));
				drawTextLine(
					hdc,
					minimapLeft,
					minimapTop + minimapSize + 10,
					"FIND COMPUTER",
					RGB(255, 220, 88));
			}
		}

		const auto contentLeft = rightPanel.left + margin;
		if (rightPanelWidth < margin * 2 + 40) {
			return;  // right panel too narrow to host the status readouts
		}

		const auto healthPercent = clampDouble(
			g_playerCombatStats.health / maxDouble(1.0, g_playerCombatStats.maxHealth),
			0.0,
			1.0);

		// Stack the status readouts vertically in the right panel, below the map.
		int cursorY = minimapTop + minimapSize + 32;
		drawTextLine(
			hdc,
			contentLeft,
			cursorY,
			"LIVES " + std::to_string(g_playerLivesRemaining),
			RGB(255, 220, 88));
		cursorY += 22;

		if (const auto* statusTexture = playerStatusHudFrameForHealth(healthPercent)) {
			const auto textureSize = static_cast<int>((std::min)(
				statusTexture->width(),
				statusTexture->height()));
			const auto statusSize = (std::min)(
				(std::min)(rightPanelWidth - margin * 2, 112),
				textureSize);
			RECT statusRect{
				contentLeft,
				cursorY,
				contentLeft + statusSize,
				cursorY + statusSize
			};
			drawTextureOpaque(hdc, *statusTexture, statusRect);
			drawTextLine(hdc, statusRect.right + 12, cursorY + 6, "ENERGY", RGB(255, 220, 88));
			drawTextLine(
				hdc,
				statusRect.right + 12,
				cursorY + 26,
				formatDouble(g_playerCombatStats.health, 0)
				+ "/" + formatDouble(g_playerCombatStats.maxHealth, 0),
				RGB(245, 245, 245));
			cursorY = statusRect.bottom + 16;
		}
		else {
			drawTextLine(hdc, contentLeft, cursorY, "ENERGY", RGB(255, 220, 88));
			const auto healthBarTop = cursorY + 20;
			const auto healthBarWidth = (std::min)(220, rightPanelWidth - margin * 2);
			RECT healthBack{
				contentLeft,
				healthBarTop,
				contentLeft + healthBarWidth,
				healthBarTop + 14
			};
			fillRect(hdc, healthBack, RGB(48, 48, 48));
			RECT healthFill = healthBack;
			healthFill.right = healthFill.left
				+ static_cast<int>(std::round((healthBack.right - healthBack.left) * healthPercent));
			fillRect(
				hdc,
				healthFill,
				healthPercent > 0.35 ? RGB(186, 32, 42) : RGB(230, 140, 32));
			drawTextLine(
				hdc,
				contentLeft,
				healthBarTop + 20,
				formatDouble(g_playerCombatStats.health, 0)
				+ "/" + formatDouble(g_playerCombatStats.maxHealth, 0),
				RGB(245, 245, 245));
			cursorY = healthBarTop + 42;
		}

		drawHudInventoryIcons(
			hdc,
			contentLeft,
			cursorY,
			rightPanelWidth - margin * 2);

		const auto& player = the3DEngine->player();
		const auto xCell = static_cast<double>(player.getX())
			/ maxDouble(1.0, static_cast<double>(theWorldMap->getCellDx()));
		const auto yCell = static_cast<double>(player.getY())
			/ maxDouble(1.0, static_cast<double>(theWorldMap->getCellDy()));
		const auto facing = cameraFacingDegrees(player);
		const auto* weapon = the3DEngine->viewWeapon();
		const auto weaponName =
			weapon != nullptr && !weapon->name().empty() ? weapon->name() : "none";

		const int lineStep = 20;
		drawTextLine(
			hdc,
			contentLeft,
			cursorY,
			"POS " + formatDouble(xCell, 2) + ", " + formatDouble(yCell, 2),
			RGB(230, 230, 230));
		cursorY += lineStep;
		drawTextLine(
			hdc,
			contentLeft,
			cursorY,
			"FACING " + std::to_string(facing) + " deg",
			RGB(230, 230, 230));
		cursorY += lineStep;
		drawTextLine(
			hdc,
			contentLeft,
			cursorY,
			"WEAPON " + weaponName,
			RGB(230, 230, 230));
		cursorY += lineStep;

		if (weapon != nullptr && weapon->usesAmmo()) {
			drawTextLine(
				hdc,
				contentLeft,
				cursorY,
				"AMMO " + std::to_string(weapon->ammoInMagazine())
				+ "/" + std::to_string(weapon->totalAmmo()),
				weapon->ammoInMagazine() > 0 ? RGB(230, 230, 230) : RGB(255, 170, 70));
			cursorY += lineStep;
		}

		const auto completion = currentCompletionStats();
		drawTextLine(
			hdc,
			contentLeft,
			cursorY,
			"KILLS "
			+ std::to_string(completion.killedEnemies)
			+ "/"
			+ std::to_string(completion.totalEnemies)
			+ " "
			+ std::to_string(completionPercent(
				completion.killedEnemies,
				completion.totalEnemies))
			+ "%",
			completion.totalEnemies > 0
			&& completion.killedEnemies >= completion.totalEnemies
			? RGB(150, 230, 150)
			: RGB(230, 230, 230));
		cursorY += lineStep;
		drawTextLine(
			hdc,
			contentLeft,
			cursorY,
			"KEYS "
			+ std::to_string(completion.collectedKeys)
			+ "/"
			+ std::to_string(completion.totalKeys)
			+ " "
			+ std::to_string(completionPercent(
				completion.collectedKeys,
				completion.totalKeys))
			+ "%",
			completion.collectedKeys >= completion.totalKeys
			? RGB(150, 230, 150)
			: RGB(230, 230, 230));
		cursorY += lineStep;

		drawTextLine(
			hdc,
			contentLeft,
			cursorY,
			std::string("FX ") + (g_soundEffectsEnabled ? "ON" : "OFF")
			+ "  TTS " + g_textToSpeechPlayer.backendName()
			+ (g_playerImmortal ? "  IMMORTAL" : ""),
			g_playerImmortal ? RGB(255, 210, 80) : RGB(230, 230, 230));
		cursorY += lineStep;

		if (!g_activeLayerId.empty()) {
			const auto layerText = g_pendingLayerTransition.active
				? "LAYER " + g_activeLayerId + " -> " + g_pendingLayerTransition.targetLayer
				: "LAYER " + g_activeLayerId;
			drawTextLine(
				hdc,
				contentLeft,
				cursorY,
				layerText,
				g_pendingLayerTransition.active ? RGB(255, 190, 72) : RGB(230, 230, 230));
			cursorY += lineStep;
		}

		drawElevatorSelectionPanel(hdc, viewport);
		drawSavePointPanel(hdc, viewport);

		// Performance readout (toggle with F6): smoothed FPS and per-phase
		// frame-time breakdown. ren = engine render, prs = present (GDI blit),
		// hud = this overlay (one frame behind), upd = input + actor AI.
		if (g_showPerfHud) {
			cursorY += 8;
			char perf[96];
			std::snprintf(perf, sizeof(perf), "FPS %.0f  (%.2f ms)",
				g_frameStats.fps, g_frameStats.frameMs);
			drawTextLine(hdc, contentLeft, cursorY, perf,
				g_frameStats.fps >= 60.0 ? RGB(150, 230, 150) : RGB(255, 200, 90));
			cursorY += lineStep;
			std::snprintf(perf, sizeof(perf), "upd %.2f  ren %.2f",
				g_frameStats.updateMs, g_frameStats.renderMs);
			drawTextLine(hdc, contentLeft, cursorY, perf, RGB(185, 195, 205));
			cursorY += lineStep;
			std::snprintf(perf, sizeof(perf), "prs %.2f  hud %.2f",
				g_frameStats.presentMs, g_frameStats.hudMs);
			drawTextLine(hdc, contentLeft, cursorY, perf, RGB(185, 195, 205));
		}
	}
}


/* -------------------------------------------------------------------------- */

static
void ChangeToFullScreen()
{
	DEVMODE dmSettings;
	memset(&dmSettings, 0, sizeof(dmSettings));

	if (!EnumDisplaySettings(NULL, ENUM_CURRENT_SETTINGS, &dmSettings)) {
		MessageBox(NULL, "Could Not Enum Display Settings", "Error", MB_OK);
		return;
	}

	dmSettings.dmPelsWidth = X_RES;
	dmSettings.dmPelsHeight = Y_RES;

	int result = ChangeDisplaySettings(&dmSettings, CDS_FULLSCREEN);

	if (result != DISP_CHANGE_SUCCESSFUL) {
		MessageBox(NULL, "Display Mode Not Compatible", "Error", MB_OK);
	}
}


/* -------------------------------------------------------------------------- */

namespace {
	struct LoadedSpriteSet {
		DirectionalSpriteFrames frames;
		std::vector<SpriteAnimationClip> animations;
		Color transparentColor = makeColor(0, 0, 0);
	};

	uint32_t pickSpriteResolution(const SpriteSet& spriteSet)
	{
		if (spriteSet.defaultResolution() != 0) {
			return spriteSet.defaultResolution();
		}

		if (spriteSet.maxResolution() != 0) {
			return spriteSet.maxResolution();
		}

		if (!spriteSet.supportedResolutions().empty()) {
			return spriteSet.supportedResolutions().front();
		}

		return 64;
	}

	bool buildDirectionalFrames(
		const SpriteSet& spriteSet,
		const std::vector<SpriteDirectionDefinition>& directions,
		uint32_t resolution,
		const std::string& spriteSetDir,
		WorldMap& worldMap,
		MapCell::TextureResourceKey& nextTextureKey,
		const std::function<std::shared_ptr<Texture>(const std::string&, int, int)>& loadTextureFromPath,
		DirectionalSpriteFrames& frames)
	{
		auto orderedDirections = directions;
		std::sort(orderedDirections.begin(), orderedDirections.end(),
			[](const SpriteDirectionDefinition& lhs, const SpriteDirectionDefinition& rhs) {
				return lhs.angleDegrees < rhs.angleDegrees;
			});

		std::vector<SpriteFrame> directionFrames;
		directionFrames.reserve(orderedDirections.size());
		for (const auto& direction : orderedDirections) {
			const auto* file = spriteSet.fileFor(direction, resolution);
			if (file == nullptr || file->empty()) {
				return false;
			}

			const auto bitmapPath = joinPath(spriteSetDir, *file);
			const auto textureKey = nextTextureKey++;
			worldMap.applyTexture(
				textureKey,
				loadTextureFromPath(
					bitmapPath,
					static_cast<int>(resolution),
					static_cast<int>(resolution)));
			directionFrames.push_back({ textureKey });
		}

		if (directionFrames.empty()) {
			return false;
		}

		frames = DirectionalSpriteFrames(std::move(directionFrames));
		return true;
	}

	int playerAlphaForFacingDegrees(const Player& player, double facingDegrees)
	{
		auto normalized = std::fmod(facingDegrees, 360.0);
		if (normalized < 0.0) {
			normalized += 360.0;
		}

		const auto facingRay = static_cast<int>(
			std::round(normalized * static_cast<double>(player.deg360()) / 360.0));
		return facingRay - player.degHalfVisual();
	}

	double readJsonDouble(
		const nlohmann::json& node,
		const char* name,
		double fallback)
	{
		if (!node.contains(name) || !node[name].is_number()) {
			return fallback;
		}

		return node[name].get<double>();
	}

	int readJsonInt(
		const nlohmann::json& node,
		const char* name,
		int fallback)
	{
		if (!node.contains(name) || !node[name].is_number_integer()) {
			return fallback;
		}

		return node[name].get<int>();
	}

	bool readJsonBool(
		const nlohmann::json& node,
		const char* name,
		bool fallback)
	{
		if (!node.contains(name) || !node[name].is_boolean()) {
			return fallback;
		}

		return node[name].get<bool>();
	}

	std::string readJsonString(
		const nlohmann::json& node,
		const char* name,
		const std::string& fallback)
	{
		if (!node.contains(name) || !node[name].is_string()) {
			return fallback;
		}

		return node[name].get<std::string>();
	}

	SceneLoader::PlayerStart readPlayerStart(
		const nlohmann::json& node,
		const SceneLoader::PlayerStart& fallback)
	{
		SceneLoader::PlayerStart playerStart = fallback;
		if (!node.is_object()) {
			return playerStart;
		}

		playerStart.xCell = readJsonDouble(node, "xCell", playerStart.xCell);
		playerStart.yCell = readJsonDouble(node, "yCell", playerStart.yCell);
		playerStart.facingDegrees =
			readJsonDouble(node, "facingDegrees", playerStart.facingDegrees);
		return playerStart;
	}

	void appendMissionSpriteObjectives(
		const std::string& worldPath,
		const std::string& layerId,
		const nlohmann::json& node,
		bool includeEnemies = true,
		bool includeKeys = true,
		bool includeItems = true,
		bool includeDestructibleProps = true)
	{
		if (!node.is_object()
			|| !node.contains("spriteInstances")
			|| !node["spriteInstances"].is_array()) {
			return;
		}

		for (const auto& entry : node["spriteInstances"]) {
			if (!entry.is_object()) {
				continue;
			}

			if (entry.contains("visible")
				&& entry["visible"].is_boolean()
				&& !entry["visible"].get<bool>()) {
				continue;
			}

			const auto name = readJsonString(entry, "name", std::string());
			const auto spriteSet =
				readJsonString(entry, "spriteSet", std::string());
			const auto maxHealth =
				readJsonDouble(entry, "maxHealth", 0.0);
			const auto chasePlayer =
				entry.contains("chasePlayer")
				&& entry["chasePlayer"].is_boolean()
				&& entry["chasePlayer"].get<bool>();
			const auto isEnemy = maxHealth > 0.0 && chasePlayer;

			SceneLoader::SpriteInstance instance;
			instance.name = name;
			instance.spriteSet = spriteSet;
			instance.xCell = readJsonDouble(entry, "xCell", 0.0);
			instance.yCell = readJsonDouble(entry, "yCell", 0.0);
			instance.pickupHealth = readJsonDouble(entry, "pickupHealth", 0.0);
			instance.pickupWeapon = readJsonString(entry, "pickupWeapon", std::string());
			instance.unlocksMap = readJsonBool(entry, "unlocksMap", false);
			const auto persistenceKey =
				runtimeSpritePersistenceKey(worldPath, layerId, instance);

			if (persistenceKey.empty()) {
				continue;
			}

			if (includeEnemies && isEnemy
				&& !vectorContainsString(
					g_missionObjectives.enemyPersistenceKeys,
					persistenceKey)) {
				g_missionObjectives.enemyPersistenceKeys.push_back(persistenceKey);
			}

			if (includeKeys && isKeyPickupIdentity(name, spriteSet)
				&& !vectorContainsString(
					g_missionObjectives.keyPersistenceKeys,
					persistenceKey)) {
				g_missionObjectives.keyPersistenceKeys.push_back(persistenceKey);
			}

			RuntimeSpriteInfo runtimeInfo;
			runtimeInfo.name = name;
			runtimeInfo.spriteSet = spriteSet;
			runtimeInfo.pickupHealth = instance.pickupHealth;
			runtimeInfo.pickupWeapon = instance.pickupWeapon;
			runtimeInfo.unlocksMap = instance.unlocksMap;
			if (includeItems && isCompletionItem(runtimeInfo)
				&& !vectorContainsString(
					g_missionObjectives.itemPersistenceKeys,
					persistenceKey)) {
				g_missionObjectives.itemPersistenceKeys.push_back(persistenceKey);
			}

			const auto damageReactive = readJsonBool(entry, "explosive", false)
				|| (entry.contains("damageResponse") && entry["damageResponse"].is_object());
			if (includeDestructibleProps && damageReactive && !isEnemy
				&& !isCompletionItem(runtimeInfo)
				&& !vectorContainsString(
					g_missionObjectives.destructiblePropPersistenceKeys,
					persistenceKey)) {
				g_missionObjectives.destructiblePropPersistenceKeys.push_back(persistenceKey);
			}
		}
	}

	void loadMissionObjectivesFromWorldDocument(
		const std::string& worldPath,
		const nlohmann::json& document)
	{
		g_missionObjectives = {};

		if (!document.is_object()) {
			return;
		}

		if (!document.contains("layers")
			|| !document["layers"].is_array()
			|| document["layers"].empty()) {
			appendMissionSpriteObjectives(worldPath, std::string(), document);
			return;
		}

		appendMissionSpriteObjectives(
			worldPath,
			std::string(),
			document,
			false,
			true);
		for (const auto& layer : document["layers"]) {
			if (!layer.is_object()) {
				continue;
			}

			const auto layerId = readJsonString(layer, "id", std::string());
			appendMissionSpriteObjectives(
				worldPath,
				layerId,
				document,
				true,
				false,
				false,
				false);
			appendMissionSpriteObjectives(worldPath, layerId, layer);
		}
	}

	void loadLayerTransitionsFromWorldFile(const std::string& worldPath)
	{
		g_layerTransitions.clear();
		g_gameGoal = {};
		g_pendingLayerTransition = {};
		g_elevatorPanel = {};
		g_layerDisplayNames.clear();
		g_missionObjectives = {};
		g_layerTransitionArmed = true;
		g_elevatorShake = {};

		std::ifstream input(worldPath);
		if (!input.is_open()) {
			return;
		}

		nlohmann::json document;
		try {
			input >> document;
		}
		catch (...) {
			return;
		}

		loadMissionObjectivesFromWorldDocument(worldPath, document);
		if (document.contains("gameGoal") && document["gameGoal"].is_object()) {
			const auto& goal = document["gameGoal"];
			g_gameGoal.layerId = readJsonString(goal, "layer", std::string());
			g_gameGoal.requiredKey = readJsonString(goal, "requiredKey", std::string());
			g_gameGoal.row = readJsonInt(goal, "row", -1);
			g_gameGoal.column = readJsonInt(goal, "column", -1);
			g_gameGoal.configured = !g_gameGoal.layerId.empty()
				&& g_gameGoal.row >= 0 && g_gameGoal.column >= 0;
		}

		if (!document.is_object() || !document.contains("layers")) {
			g_activeLayerId.clear();
			return;
		}

		if (document["layers"].is_array()) {
			for (const auto& layer : document["layers"]) {
				if (!layer.is_object()) {
					continue;
				}

				const auto id = readJsonString(layer, "id", std::string());
				if (!id.empty()) {
					g_layerDisplayNames[id] =
						readJsonString(layer, "name", std::string());
				}
			}
		}

		if (g_activeLayerId.empty()) {
			g_activeLayerId = readJsonString(document, "startLayer", std::string());
		}

		if (g_activeLayerId.empty()) {
			g_activeLayerId = readJsonString(document, "activeLayer", std::string());
		}

		if (g_activeLayerId.empty()
			&& document["layers"].is_array()
			&& !document["layers"].empty()
			&& document["layers"].front().is_object()) {
			g_activeLayerId =
				readJsonString(document["layers"].front(), "id", std::string());
		}

		if (!document.contains("layerTransitions")
			|| !document["layerTransitions"].is_array()) {
			return;
		}

		SceneLoader::PlayerStart defaultStart;
		if (document.contains("playerStart") && document["playerStart"].is_object()) {
			defaultStart = readPlayerStart(document["playerStart"], defaultStart);
		}

		for (const auto& entry : document["layerTransitions"]) {
			if (!entry.is_object()) {
				continue;
			}

			LayerTransition transition;
			transition.fromLayer = readJsonString(entry, "fromLayer", std::string());
			transition.toLayer = readJsonString(entry, "toLayer", std::string());
			transition.requiredKey = readJsonString(entry, "requiredKey", std::string());
			transition.triggerBlockId =
				readJsonString(entry, "triggerBlockId", std::string());
			transition.waitSeconds = readJsonDouble(entry, "waitSeconds", 1.5);

			if (entry.contains("trigger") && entry["trigger"].is_object()) {
				const auto& trigger = entry["trigger"];
				if (transition.triggerBlockId.empty()) {
					transition.triggerBlockId =
						readJsonString(trigger, "blockId", std::string());
				}

				transition.triggerRow = readJsonInt(trigger, "row", -1);
				transition.triggerColumn = readJsonInt(trigger, "column", -1);
				transition.hasTriggerCell =
					transition.triggerRow >= 0 && transition.triggerColumn >= 0;
			}

			if (entry.contains("targetPlayerStart")
				&& entry["targetPlayerStart"].is_object()) {
				transition.targetPlayerStart =
					readPlayerStart(entry["targetPlayerStart"], defaultStart);
				transition.hasTargetPlayerStart = true;
			}

			if (!transition.fromLayer.empty()
				&& !transition.toLayer.empty()
				&& !transition.triggerBlockId.empty()) {
				g_layerTransitions.push_back(std::move(transition));
			}
		}
	}

	bool tryParseBlockId(const std::string& text, uint8_t& blockId) noexcept
	{
		if (text.empty() || text.size() > 2) {
			return false;
		}

		for (char ch : text) {
			if (!std::isxdigit(static_cast<unsigned char>(ch))) {
				return false;
			}
		}

		try {
			const auto value = std::stoul(text, nullptr, 16);
			if (value > 0xff) {
				return false;
			}

			blockId = static_cast<uint8_t>(value);
			return true;
		}
		catch (...) {
			return false;
		}
	}

	std::unique_ptr<ViewWeapon> loadViewWeaponFromMetadata(
		const std::string& metadataPath)
	{
		std::ifstream input(metadataPath);
		if (!input.is_open()) {
			return {};
		}

		nlohmann::json document;
		try {
			input >> document;
		}
		catch (...) {
			return {};
		}

		if (!document.is_object() || !document.contains("animations")
			|| !document["animations"].is_object()) {
			return {};
		}

		const auto metadataDir = directoryOf(metadataPath);
		const auto frameWidth = readJsonInt(document, "frameWidth", 320);
		const auto frameHeight = readJsonInt(document, "frameHeight", 220);
		if (frameWidth <= 0 || frameHeight <= 0) {
			return {};
		}

		auto weapon = std::make_unique<ViewWeapon>();
		weapon->setName(readJsonString(document, "weapon", "view_weapon"));
		weapon->setScreenHeightFraction(
			readJsonDouble(document, "screenHeightFraction", 0.45));
		weapon->setDamage(readJsonDouble(document, "damage", 0.0));
		weapon->setRangeCells(readJsonDouble(document, "rangeCells", 8.0));
		if (document.contains("fireBehavior")
			&& document["fireBehavior"].is_object()) {
			const auto& fireBehavior = document["fireBehavior"];
			weapon->setFireBehavior(
				readJsonBool(fireBehavior, "automatic", false),
				readJsonDouble(fireBehavior, "intervalMs", 0.0),
				readJsonDouble(fireBehavior, "soundIntervalMs", 0.0));
		}
		else {
			weapon->setFireBehavior(
				readJsonBool(document, "automaticFire", false),
				readJsonDouble(document, "fireIntervalMs", 0.0));
		}

		if (document.contains("sounds") && document["sounds"].is_object()) {
			const auto fireSound =
				readJsonString(document["sounds"], "fire", std::string());
			if (!fireSound.empty()) {
				weapon->setFireSoundPath(joinPath(metadataDir, fireSound));
			}
		}

		if (document.contains("ammo") && document["ammo"].is_object()) {
			const auto& ammo = document["ammo"];
			weapon->setAmmo(
				readJsonInt(ammo, "magazineSize", 0),
				readJsonInt(ammo, "maxAmmo", 0),
				readJsonInt(ammo, "initialAmmo", -1));
		}

		if (document.contains("anchor") && document["anchor"].is_object()) {
			weapon->setAnchor(
				readJsonDouble(document["anchor"], "x", 0.5),
				readJsonDouble(document["anchor"], "y", 1.0));
		}

		if (document.contains("baseOffset") && document["baseOffset"].is_object()) {
			weapon->setBaseOffset(
				readJsonDouble(document["baseOffset"], "x", 0.0),
				readJsonDouble(document["baseOffset"], "y", 0.0));
		}

		if (document.contains("bob") && document["bob"].is_object()) {
			weapon->setBob(
				readJsonBool(document["bob"], "enabled", true),
				readJsonDouble(document["bob"], "amount", 1.0),
				readJsonDouble(document["bob"], "amplitudeX", 6.0),
				readJsonDouble(document["bob"], "amplitudeY", 4.0),
				readJsonDouble(document["bob"], "frequencyHz", 3.0));
		}

		for (const auto& animationItem : document["animations"].items()) {
			if (!animationItem.value().is_object()
				|| !animationItem.value().contains("files")
				|| !animationItem.value()["files"].is_array()) {
				continue;
			}

			ViewWeapon::Animation animation;
			animation.name = animationItem.key();
			animation.frameDurationMs =
				readJsonDouble(animationItem.value(), "frameDurationMs", 100.0);
			animation.loop = readJsonBool(animationItem.value(), "loop", true);

			for (const auto& fileEntry : animationItem.value()["files"]) {
				if (!fileEntry.is_string()) {
					continue;
				}

				const auto framePath = joinPath(metadataDir, fileEntry.get<std::string>());
				auto texture = loadTextureFromFile(framePath, frameWidth, frameHeight);
				if (texture) {
					texture->setHasAlpha(true);
					animation.frames.push_back(std::move(texture));
				}
			}

			weapon->addAnimation(std::move(animation));
		}

		if (!weapon->setAnimation("idle")) {
			return {};
		}

		return weapon;
	}

	void syncActivePlayerWeaponFromEngine() noexcept
	{
		if (!the3DEngine
			|| !the3DEngine->viewWeapon()
			|| g_activePlayerWeaponIndex >= g_playerWeapons.size()) {
			return;
		}

		g_playerWeapons[g_activePlayerWeaponIndex].weapon =
			*the3DEngine->viewWeapon();
	}

	bool playerWeaponFileMatches(
		const std::string& lhs,
		const std::string& rhs)
	{
		return normalizeResourcePathForCompare(lhs)
			== normalizeResourcePathForCompare(rhs);
	}

	size_t findPlayerWeaponIndexByFile(const std::string& weaponFile)
	{
		if (weaponFile.empty()) {
			return g_playerWeapons.size();
		}

		for (size_t index = 0; index < g_playerWeapons.size(); ++index) {
			if (playerWeaponFileMatches(g_playerWeapons[index].file, weaponFile)) {
				return index;
			}
		}

		return g_playerWeapons.size();
	}

	bool hasEquippedPlayerWeapon() noexcept
	{
		return the3DEngine != nullptr && the3DEngine->viewWeapon() != nullptr;
	}

	bool activatePlayerWeapon(size_t weaponIndex, bool syncCurrent = true)
	{
		if (!the3DEngine || weaponIndex >= g_playerWeapons.size()) {
			return false;
		}

		if (!g_playerWeapons[weaponIndex].unlocked) {
			return false;
		}

		if (syncCurrent) {
			syncActivePlayerWeaponFromEngine();
		}
		g_activePlayerWeaponIndex = weaponIndex;
		the3DEngine->setViewWeapon(g_playerWeapons[weaponIndex].weapon);
		g_weaponFireWasPressed = false;
		g_weaponReloadWasPressed = false;
		g_weaponAutoReloadPending = false;
		return true;
	}

	bool equipFirstUnlockedPlayerWeapon()
	{
		for (size_t index = 0; index < g_playerWeapons.size(); ++index) {
			if (g_playerWeapons[index].unlocked) {
				return activatePlayerWeapon(index, false);
			}
		}

		if (the3DEngine) {
			the3DEngine->clearViewWeapon();
		}

		return false;
	}

	bool unlockPlayerWeaponByFile(
		const std::string& weaponFile,
		size_t* unlockedIndex = nullptr)
	{
		const auto weaponIndex = findPlayerWeaponIndexByFile(weaponFile);
		if (weaponIndex >= g_playerWeapons.size()) {
			return false;
		}

		const auto wasUnlocked = g_playerWeapons[weaponIndex].unlocked;
		g_playerWeapons[weaponIndex].unlocked = true;
		if (unlockedIndex != nullptr) {
			*unlockedIndex = weaponIndex;
		}

		return !wasUnlocked;
	}

	void restorePlayerWeaponAmmo(
		const std::map<std::string, std::pair<int, int>>& ammoByWeaponFile)
	{
		for (auto& playerWeapon : g_playerWeapons) {
			const auto ammo = ammoByWeaponFile.find(playerWeapon.file);
			if (ammo != ammoByWeaponFile.end()
				&& playerWeapon.weapon.usesAmmo()) {
				playerWeapon.weapon.setAmmoCounts(
					ammo->second.first,
					ammo->second.second);
			}
		}
	}

	void restorePlayerWeaponUnlockedState(
		const std::map<std::string, bool>& unlockedByWeaponFile)
	{
		for (auto& playerWeapon : g_playerWeapons) {
			for (const auto& item : unlockedByWeaponFile) {
				if (playerWeaponFileMatches(playerWeapon.file, item.first)) {
					playerWeapon.unlocked = item.second;
					break;
				}
			}
		}
	}

	void loadPlayerWeaponsForScene(
		const SceneLoader::Scene& scene,
		const std::string& projectDir)
	{
		g_playerWeapons.clear();
		g_activePlayerWeaponIndex = 0;
		if (the3DEngine) {
			the3DEngine->clearViewWeapon();
		}

		auto weaponConfigs = scene.playerWeapons;
		if (weaponConfigs.empty() && !scene.playerWeapon.file.empty()) {
			weaponConfigs.push_back(scene.playerWeapon);
		}

		size_t activeWeaponIndex = 0;
		for (const auto& weaponConfig : weaponConfigs) {
			if (!weaponConfig.visible || weaponConfig.file.empty()) {
				continue;
			}

			const auto weaponPath = joinPath(projectDir, weaponConfig.file);
			auto viewWeapon = loadViewWeaponFromMetadata(weaponPath);
			if (!viewWeapon) {
				MessageBox(
					g_hWnd,
					("Failed to load player weapon metadata:\n" + weaponPath).c_str(),
					g_szAppTitle,
					MB_OK | MB_ICONWARNING);
				continue;
			}

			if (weaponConfig.screenHeightFraction > 0.0) {
				viewWeapon->setScreenHeightFraction(
					weaponConfig.screenHeightFraction);
			}

			if (weaponConfig.file == scene.playerWeapon.file) {
				activeWeaponIndex = g_playerWeapons.size();
			}

			RuntimePlayerWeapon playerWeapon;
			playerWeapon.file = weaponConfig.file;
			playerWeapon.weapon = std::move(*viewWeapon);
			playerWeapon.unlocked = weaponConfig.unlocked;
			g_playerWeapons.push_back(std::move(playerWeapon));
		}

		if (!g_playerWeapons.empty()) {
			g_activePlayerWeaponIndex =
				(std::min)(activeWeaponIndex, g_playerWeapons.size() - 1);
			if (!activatePlayerWeapon(g_activePlayerWeaponIndex, false)) {
				equipFirstUnlockedPlayerWeapon();
			}
		}
	}
}

static
bool Setup3DEngine(const std::string& worldPath,
	const std::string& worldDir,
	const SceneLoader::Scene* scene,
	const std::string& projectDir,
	const std::string& layerId = std::string(),
	const SceneLoader::PlayerStart* playerStartOverride = nullptr,
	bool preservePlayerStats = false)
{
	g_spriteActors.clear();
	g_runtimeSpriteInfos.clear();
	g_eventLog.clear();
	if (!preservePlayerStats) {
		g_minimapUnlocked = false;
		g_minimapActorsUnlocked = false;
		g_keyHudTextures.clear();
	}

	auto worldMap = std::make_unique<WorldMap>();

	Player aCamera = Player(0, 0, VISUAL_DEGREE, PROJ_X_RES, PROJ_Y_RES);
	aCamera.setPos(
		make_pair<int, int>(CELL_SIZE * CAMERA_CEL_COL_POS,
			CELL_SIZE * CAMERA_CEL_ROW_POS)
	);

	const bool worldIsJson = endsWithIgnoreCase(worldPath, ".world.json")
		|| endsWithIgnoreCase(worldPath, ".json");
	if (!worldIsJson) {
		MessageBox(
			g_hWnd,
			("Unsupported world format:\n" + worldPath
				+ "\nOnly JSON world files are supported.").c_str(),
			g_szAppTitle,
			MB_OK | MB_ICONERROR);
		return false;
	}

	WorldJsonLoader jsonLoader;
	const auto loadResult = jsonLoader.loadFromFile(worldPath, *worldMap, layerId);
	if (!loadResult.success) {
		std::string message = "Failed to load world JSON: " + worldPath;
		for (const auto& error : loadResult.errors) {
			message += "\n";
			message += error;
		}

		MessageBox(g_hWnd, message.c_str(), g_szAppTitle, MB_OK | MB_ICONERROR);
		return false;
	}

	if (!loadResult.activeLayerId.empty()) {
		g_activeLayerId = loadResult.activeLayerId;
	}

	if (playerStartOverride != nullptr) {
		worldMap->setPlayerStartCell(
			playerStartOverride->xCell,
			playerStartOverride->yCell,
			playerStartOverride->facingDegrees);
	}
	else if (scene != nullptr && scene->hasPlayerStart) {
		worldMap->setPlayerStartCell(
			scene->playerStart.xCell,
			scene->playerStart.yCell,
			scene->playerStart.facingDegrees);
	}

	if (worldMap->hasPlayerStart()) {
		const auto& playerStart = worldMap->getPlayerCellPos();
		aCamera.setPos(make_pair<int, int>(
			static_cast<int>(playerStart.first * worldMap->getCellDx()),
			static_cast<int>(playerStart.second * worldMap->getCellDy())));
		aCamera.setAlpha(playerAlphaForFacingDegrees(aCamera, worldMap->getPlayerFacingDegrees()));
	}

	if (!preservePlayerStats) {
		g_playerCombatStats = {};
		g_missionElapsedSeconds = 0.0;
		g_completionSummary = {};
		g_playerLifeState = PlayerLifeState::Alive;
		g_playerDeathElapsedSeconds = 0.0;
		g_playerDeathMessageShown = false;
		g_gameCompleted = false;
		g_gameCompletedMessageShown = false;
		g_gameOver = false;
		g_playerLivesRemaining = kInitialPlayerLives;
		g_activeSavePointPromptKey.clear();
		g_savePointPanel = {};
		g_savedStateSignatureByPoint.clear();
	}

	if (!preservePlayerStats && scene != nullptr) {
		g_playerCombatStats.maxHealth =
			maxDouble(1.0, scene->playerStats.maxHealth);
		g_playerCombatStats.health = clampDouble(
			scene->playerStats.health,
			0.0,
			g_playerCombatStats.maxHealth);
	}

	const auto& textureList = worldMap->getTextureList();

	auto hasImageExtension = [](const std::string& image) {
		const auto dot = image.find_last_of('.');
		if (dot == std::string::npos) {
			return false;
		}

		auto extension = image.substr(dot);
		std::transform(extension.begin(), extension.end(), extension.begin(),
			[](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
		return extension == ".bmp" || extension == ".png";
		};

	auto loadTextureRelative = [&worldDir, &hasImageExtension](const std::string& image,
		const int dx = CELL_SIZE,
		const int dy = CELL_SIZE,
		const char* ext = ".bmp")
		{
			std::string image_name = worldDir;
			image_name += image;
			if (!hasImageExtension(image_name)) {
				image_name += ext;
			}

			return loadTextureFromFile(image_name, dx, dy);
		};

	auto loadTextureFromPath = [](const std::string& fullPath,
		const int dx, const int dy)
		{
			return loadTextureFromFile(fullPath, dx, dy);
		};

	for (const auto& item : textureList) {
		worldMap->applyTextureToPanel(
			stoi(item.first, 0, 16),
			loadTextureRelative(item.second)
		);
	}

#define SKY_BMP_RESOURCE "clouds"

	if (!worldMap->getTexture(MapCell::TRANSPARENT_TEXTURE_KEY)) {
		worldMap->applyTextureToPanel(
			MapCell::TRANSPARENT_TEXTURE_KEY,
			loadTextureRelative(SKY_BMP_RESOURCE, PROJ_X_RES, PROJ_Y_RES)
		);
	}

	the3DEngine = std::make_unique<RaycastEngine>(aCamera, SCALE);
	if (scene != nullptr) {
		the3DEngine->setBrightness(scene->brightness);
		the3DEngine->setDepthShadingLevel(scene->depthShading);
	}

	if (scene == nullptr) {
		std::vector<SpriteFrame> spriteViews;
		for (int view = 0; view < TEST_SPRITE_VIEW_COUNT; ++view) {
			const auto textureKey =
				static_cast<MapCell::TextureResourceKey>(TEST_SPRITE_TEXTURE_BASE + view);
			worldMap->applyTexture(
				textureKey,
				loadTextureRelative("sprite_test_" + std::to_string(view), 64, 64));
			spriteViews.push_back({ textureKey });
		}

		Sprite testSprite;
		testSprite.x = CELL_SIZE * 5.5;
		testSprite.y = CELL_SIZE * 4.5;
		testSprite.scale = CELL_SIZE;
		testSprite.transparentColor = makeColor(0, 0, 0);
		testSprite.frames = DirectionalSpriteFrames(std::move(spriteViews));

		the3DEngine->addSprite(testSprite);
	}
	else {
		std::map<std::string, LoadedSpriteSet> spriteSetIndex;
		auto nextTextureKey =
			static_cast<MapCell::TextureResourceKey>(TEST_SPRITE_TEXTURE_BASE);

		for (const auto& spriteSetRelative : scene->spriteSets) {
			const auto spriteSetPath = joinPath(projectDir, spriteSetRelative);
			const auto spriteSetDir = directoryOf(spriteSetPath);

			SpriteMetadataLoader spriteLoader;
			const auto loadResult = spriteLoader.loadFromFile(spriteSetPath);
			if (!loadResult.success) {
				std::string message = "Failed to load sprite metadata: " + spriteSetPath;
				for (const auto& error : loadResult.errors) {
					message += "\n";
					message += error;
				}

				MessageBox(g_hWnd, message.c_str(), g_szAppTitle, MB_OK | MB_ICONERROR);
				continue;
			}

			const auto& spriteSet = loadResult.spriteSet;
			const auto resolution = pickSpriteResolution(spriteSet);
			DirectionalSpriteFrames baseFrames;
			if (!buildDirectionalFrames(
				spriteSet,
				spriteSet.directions(),
				resolution,
				spriteSetDir,
				*worldMap,
				nextTextureKey,
				loadTextureFromPath,
				baseFrames)) {
				continue;
			}

			LoadedSpriteSet entry;
			entry.frames = std::move(baseFrames);
			entry.transparentColor = spriteSet.transparentColor();

			for (const auto& animation : spriteSet.animations()) {
				DirectionalSpriteFrames animationFrames;
				if (!buildDirectionalFrames(
					spriteSet,
					animation.directions,
					resolution,
					spriteSetDir,
					*worldMap,
					nextTextureKey,
					loadTextureFromPath,
					animationFrames)) {
					continue;
				}

				SpriteAnimationClip clip;
				clip.name = animation.name;
				clip.frameDurationMs = animation.frameDurationMs;
				clip.loop = animation.loop;
				clip.frames = std::move(animationFrames);

				for (const auto& frameDirections : animation.frames) {
					DirectionalSpriteFrames frameSet;
					if (!buildDirectionalFrames(
						spriteSet,
						frameDirections,
						resolution,
						spriteSetDir,
						*worldMap,
						nextTextureKey,
						loadTextureFromPath,
						frameSet)) {
						continue;
					}

					clip.frameSets.push_back(std::move(frameSet));
				}

				entry.animations.push_back(std::move(clip));
			}

			spriteSetIndex[spriteSet.name()] = std::move(entry);
		}

		auto addSpriteFromLoadedSet =
			[&](const LoadedSpriteSet& loadedSet,
				const SceneLoader::SpriteInstance& instance,
				bool visible,
				double scaleCells,
				double collisionRadiusCells) {
					Sprite sprite;
					sprite.x = CELL_SIZE * instance.xCell;
					sprite.y = CELL_SIZE * instance.yCell;
					sprite.scale = CELL_SIZE * scaleCells;
					sprite.verticalOffset = CELL_SIZE * instance.verticalOffsetCells;
					sprite.facingRadians =
						instance.facingDegrees * 3.14159265358979323846 / 180.0;
					sprite.collisionRadius = CELL_SIZE * collisionRadiusCells;
					sprite.visible = visible;
					sprite.transparentColor = loadedSet.transparentColor;
					sprite.frames = loadedSet.frames;
					sprite.animations = loadedSet.animations;
					sprite.setAnimationOrFallback("idle", "");

					const auto spriteIndex = the3DEngine->sprites().size();
					the3DEngine->addSprite(sprite);
					return spriteIndex;
			};

		for (const auto& instance : scene->spriteInstances) {
			if (!instance.visible) {
				continue;
			}

			const auto entry = spriteSetIndex.find(instance.spriteSet);
			if (entry == spriteSetIndex.end()) {
				continue;
			}

			const auto spriteIndex = addSpriteFromLoadedSet(
				entry->second,
				instance,
				instance.visible,
				instance.scaleCells,
				instance.collisionRadiusCells);
			auto* sprite = the3DEngine->sprite(spriteIndex);
			if (sprite == nullptr) {
				continue;
			}

			RuntimeSpriteInfo runtimeInfo;
			runtimeInfo.spriteIndex = spriteIndex;
			runtimeInfo.name = instance.name;
			runtimeInfo.spriteSet = instance.spriteSet;
			const auto isActor = instance.chasePlayer
				|| instance.patrolCircuit
				|| instance.maxHealth > 0.0;
			// Pickups defined at world scope remain global. Actors must be scoped
			// to the active floor so damage and death cannot leak between layers.
			runtimeInfo.layerId = instance.layerId.empty() && isActor
				? g_activeLayerId
				: instance.layerId;
			runtimeInfo.persistenceKey = runtimeSpritePersistenceKey(
				worldPath,
				runtimeInfo.layerId,
				instance);
			runtimeInfo.keyId = keyIdFromSpriteIdentity(
				runtimeInfo.name,
				runtimeInfo.spriteSet);
			runtimeInfo.pickupWeapon = instance.pickupWeapon;
			runtimeInfo.pickupHealth = maxDouble(0.0, instance.pickupHealth);
			runtimeInfo.unlocksMap = instance.unlocksMap;
			runtimeInfo.savePoint = instance.savePoint;
			runtimeInfo.explosive = instance.explosive;
			const auto hasDamageResponse =
				!instance.damageResponseType.empty()
				|| !instance.damageResponseEffectSpriteSet.empty()
				|| !instance.damageResponseDestroyedSpriteSet.empty()
				|| !instance.damageResponseSound.empty()
				|| instance.damageResponseHitPoints > 0.0;
			if (hasDamageResponse) {
				runtimeInfo.damageResponseType =
					instance.damageResponseType.empty()
					? std::string("break")
					: instance.damageResponseType;
			}
			if (!isActor
				&& isRuntimeDamageReactive(runtimeInfo)
				&& sprite->collisionRadius <= 0.0) {
				sprite->collisionRadius = sprite->scale * 0.28;
			}
			runtimeInfo.blocksPlayer =
				!isActor && isRuntimeDamageReactive(runtimeInfo);
			runtimeInfo.explosiveHitPoints =
				maxDouble(
					1.0,
					hasDamageResponse && instance.damageResponseHitPoints > 0.0
					? instance.damageResponseHitPoints
					: instance.explosiveHitPoints);
			runtimeInfo.explosiveHealth = runtimeInfo.explosiveHitPoints;
			const auto healthState =
				g_runtimeSpriteExplosiveHealthByKey.find(runtimeInfo.persistenceKey);
			if (healthState != g_runtimeSpriteExplosiveHealthByKey.end()) {
				runtimeInfo.explosiveHealth = clampDouble(
					healthState->second,
					0.0,
					runtimeInfo.explosiveHitPoints);
			}
			runtimeInfo.explosionRadiusCells =
				maxDouble(
					0.0,
					hasDamageResponse
					? instance.damageResponseRadiusCells
					: instance.explosionRadiusCells);
			runtimeInfo.explosionDamage =
				maxDouble(
					0.0,
					hasDamageResponse
					? instance.damageResponseDamage
					: instance.explosionDamage);
			runtimeInfo.explosionScaleCells =
				maxDouble(
					0.05,
					hasDamageResponse
					? instance.damageResponseEffectScaleCells
					: instance.explosionScaleCells);
			runtimeInfo.explosionSpriteSet =
				hasDamageResponse
				? instance.damageResponseEffectSpriteSet
				: instance.explosionSpriteSet;
			runtimeInfo.destroyedSpriteSet =
				hasDamageResponse
				? instance.damageResponseDestroyedSpriteSet
				: instance.destroyedSpriteSet;
			runtimeInfo.destroyedScaleCells =
				maxDouble(
					0.05,
					hasDamageResponse
					? instance.damageResponseDestroyedScaleCells
					: instance.destroyedScaleCells);
			runtimeInfo.damageEffectAnimation =
				hasDamageResponse ? instance.damageResponseEffectAnimation : std::string();
			runtimeInfo.damageEffectSound =
				hasDamageResponse ? instance.damageResponseSound : std::string();

			if (isRuntimeDamageReactive(runtimeInfo)
				&& !runtimeInfo.explosionSpriteSet.empty()) {
				const auto explosionEntry =
					spriteSetIndex.find(runtimeInfo.explosionSpriteSet);
				if (explosionEntry != spriteSetIndex.end()) {
					runtimeInfo.explosionSpriteIndex = addSpriteFromLoadedSet(
						explosionEntry->second,
						instance,
						false,
						runtimeInfo.explosionScaleCells,
						0.0);
				}
			}

			if (isRuntimeDamageReactive(runtimeInfo)
				&& !runtimeInfo.destroyedSpriteSet.empty()) {
				const auto destroyedEntry =
					spriteSetIndex.find(runtimeInfo.destroyedSpriteSet);
				if (destroyedEntry != spriteSetIndex.end()) {
					runtimeInfo.destroyedSpriteIndex = addSpriteFromLoadedSet(
						destroyedEntry->second,
						instance,
						false,
						runtimeInfo.destroyedScaleCells,
						0.0);
				}
			}

			const auto consumedState =
				g_runtimeSpriteConsumedByKey.find(runtimeInfo.persistenceKey);
			if (consumedState != g_runtimeSpriteConsumedByKey.end()
				&& consumedState->second) {
				sprite->visible = false;
				runtimeInfo.consumed = true;
				const auto explodedState =
					g_runtimeSpriteExplodedByKey.find(runtimeInfo.persistenceKey);
				if (explodedState != g_runtimeSpriteExplodedByKey.end()
					&& explodedState->second) {
					if (auto* destroyed =
						the3DEngine->sprite(runtimeInfo.destroyedSpriteIndex)) {
						destroyed->visible = true;
					}
				}
			}

			const auto actorPersistenceKey = runtimeInfo.persistenceKey;
			g_runtimeSpriteInfos.push_back(std::move(runtimeInfo));

			if (isActor) {
				SpriteActor actor;
				actor.spriteIndex = spriteIndex;
				actor.persistenceKey = actorPersistenceKey;
				actor.homeX = sprite->x;
				actor.homeY = sprite->y;
				actor.hasHomePosition = true;
				actor.chasePlayer = instance.chasePlayer;
				actor.speedCellsPerSecond = instance.speedCellsPerSecond;
				actor.detectionRadiusCells = instance.detectionRadiusCells;
				actor.patrolRadiusCells = instance.patrolRadiusCells;
				actor.engagementHysteresisCells = instance.engagementHysteresisCells;
				actor.patrolCircuit = instance.patrolCircuit;
				actor.stoppingDistanceCells = instance.stoppingDistanceCells;
				actor.collidesWithWorld = !instance.passThroughWalls;
				actor.maxHealth = maxDouble(0.0, instance.maxHealth);
				actor.health = actor.maxHealth > 0.0
					? clampDouble(instance.health, 0.0, actor.maxHealth)
					: 0.0;
				actor.attackDamage = maxDouble(0.0, instance.attackDamage);
				actor.rangedAttack = instance.rangedAttack;
				actor.attackRangeCells = maxDouble(0.0, instance.attackRangeCells);
				actor.attackCooldownSeconds =
					maxDouble(0.1, instance.attackCooldownSeconds);
				actor.attackFovDegrees = maxDouble(1.0, instance.attackFovDegrees);
				actor.attackBurstShots = (std::max)(1, instance.attackBurstShots);
				actor.attackBurstPauseSeconds =
					maxDouble(0.1, instance.attackBurstPauseSeconds);
				const auto state =
					g_runtimeActorStateByKey.find(actor.persistenceKey);
				if (state != g_runtimeActorStateByKey.end()) {
					if (auto* loadedSprite = the3DEngine->sprite(spriteIndex)) {
						applyRuntimeActorState(actor, *loadedSprite, state->second);
					}
				}
				g_spriteActors.push_back(actor);
			}
		}

		loadPlayerWeaponsForScene(*scene, projectDir);
	}

	theWorldMap = std::move(worldMap);
	syncWorldDoorKeyring();
	g_lastActorUpdateMs = GetTickCount64();
	g_playerMovingThisFrame = false;
	g_weaponFireWasPressed = false;
	g_weaponReloadWasPressed = false;
	g_weaponAutoReloadPending = false;
	std::fill(
		g_weaponSwitchWasPressed,
		g_weaponSwitchWasPressed + 9,
		false);
	g_damageFlashSeconds = 0.0;
	g_soundEffectWarningShown = false;
	g_lastDoorEffectTimeMs = 0;
	g_lastDoorEffectRow = -1;
	g_lastDoorEffectColumn = -1;
	setBackgroundMusicFromScene(scene, worldDir.empty() ? projectDir : worldDir);
	loadPlayerStatusHudFrames(worldDir.empty() ? projectDir : worldDir);

	return true;
}

static
bool LoadDefaultWorldIntoEngine()
{
	return LoadProjectOrWorldIntoEngine("res/worlds/demo_embedded/demo.world.json");
}

static
bool LoadProjectIntoEngine(const std::string& projectPath)
{
	const auto loadingDifferentProject = g_currentProjectPath != projectPath;
	if (loadingDifferentProject) {
		g_activeLayerId.clear();
		g_runtimeSpriteConsumedByKey.clear();
		g_runtimeSpriteExplodedByKey.clear();
		g_runtimeSpriteExplosiveHealthByKey.clear();
		g_runtimeActorStateByKey.clear();
		g_playerKeyIds.clear();
		g_autoCheckpoint = {};
		g_playerLifeState = PlayerLifeState::Alive;
		g_playerDeathElapsedSeconds = 0.0;
		g_playerDeathMessageShown = false;
		g_gameCompleted = false;
		g_gameCompletedMessageShown = false;
	}

	SceneLoader sceneLoader;
	auto sceneResult = sceneLoader.loadFromFile(projectPath, g_activeLayerId);
	if (!sceneResult.success) {
		std::string message = "Failed to load project file:\n" + projectPath;
		for (const auto& error : sceneResult.errors) {
			message += "\n";
			message += error;
		}

		MessageBox(g_hWnd, message.c_str(), g_szAppTitle, MB_OK | MB_ICONERROR);
		return false;
	}

	const auto projectDir = directoryOf(projectPath);
	std::string worldPath = looksLikeWorldJson(projectPath)
		? projectPath
		: std::string("res/worlds/demo_embedded/demo.world.json");
	std::string worldDir;
	if (!sceneResult.scene.worldFile.empty()) {
		worldPath = joinPath(projectDir, sceneResult.scene.worldFile);
		worldDir = directoryOf(worldPath);
	}
	else {
		worldDir = directoryOf(worldPath);
	}

	loadLayerTransitionsFromWorldFile(worldPath);
	if (!g_activeLayerId.empty()) {
		sceneResult = sceneLoader.loadFromFile(projectPath, g_activeLayerId);
		if (!sceneResult.success) {
			std::string message = "Failed to load project layer '"
				+ g_activeLayerId + "':\n" + projectPath;
			for (const auto& error : sceneResult.errors) {
				message += "\n";
				message += error;
			}

			MessageBox(g_hWnd, message.c_str(), g_szAppTitle, MB_OK | MB_ICONERROR);
			return false;
		}
	}

	if (!loadingDifferentProject) {
		syncRuntimeActorStates();
		syncRuntimeSpriteStates();
	}

	auto previousWorldMap = std::move(theWorldMap);
	auto previousEngine = std::move(the3DEngine);

	if (!Setup3DEngine(
		worldPath,
		worldDir,
		&sceneResult.scene,
		projectDir,
		g_activeLayerId)) {
		theWorldMap = std::move(previousWorldMap);
		the3DEngine = std::move(previousEngine);
		return false;
	}

	g_currentProjectPath = projectPath;
	g_currentWorldPath = worldPath;
	g_currentWorldDir = worldDir;
	g_currentProjectDir = projectDir;
	saveAutoCheckpoint(false);
	configureDeveloperMenus();

	return true;
}

static
bool LoadProjectOrWorldIntoEngine(const std::string& path)
{
	return LoadProjectIntoEngine(path);
}

static
void RestartGameFromBeginning()
{
	if (g_currentProjectPath.empty()) {
		return;
	}

	const auto projectPath = g_currentProjectPath;
	g_currentProjectPath.clear();
	g_gameOver = false;
	g_playerLivesRemaining = kInitialPlayerLives;
	g_autoCheckpoint = {};
	LoadProjectIntoEngine(projectPath);
}

static
bool OpenProjectFromMenu(HWND hWnd)
{
	char fileName[MAX_PATH] = { 0 };
	OPENFILENAME openFile = { 0 };
	openFile.lStructSize = sizeof(openFile);
	openFile.hwndOwner = hWnd;
	openFile.hInstance = g_hInstance;
	openFile.lpstrFilter =
		"nuRCADE project/world (*.nurcadeproj.json;*.world.json;*.json)\0*.nurcadeproj.json;*.world.json;*.json\0"
		"All files (*.*)\0*.*\0";
	openFile.lpstrFile = fileName;
	openFile.nMaxFile = MAX_PATH;
	openFile.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
	openFile.lpstrTitle = "Open nuRCADE Project or World";

	if (!GetOpenFileName(&openFile)) {
		return false;
	}

	return LoadProjectOrWorldIntoEngine(fileName);
}

static
void ToggleBackgroundMusic()
{
	g_backgroundMusicEnabled = !g_backgroundMusicEnabled;
	applyBackgroundMusicState();
}

static
void ToggleSoundEffects()
{
	g_soundEffectsEnabled = !g_soundEffectsEnabled;
	updateBackgroundMusicMenu();
}

static
void ToggleEventSpeech()
{
	g_eventSpeechEnabled = !g_eventSpeechEnabled;
	if (!g_eventSpeechEnabled) {
		g_textToSpeechPlayer.stop();
	}
	updateBackgroundMusicMenu();
}

static
void TogglePlayerImmortal()
{
	if (!g_developerMode) {
		return;
	}

	g_playerImmortal = !g_playerImmortal;
	updateBackgroundMusicMenu();
}

static
void GivePlayerAllKeys()
{
	if (!g_developerMode) {
		return;
	}

	static constexpr const char* kAllKeyIds[] = {
		"green",
		"blue",
		"red",
		"yellow"
	};

	const auto previousCount = g_playerKeyIds.size();
	for (const auto* keyId : kAllKeyIds) {
		addPlayerKeyId(keyId);
	}

	pushEventMessage(
		g_playerKeyIds.size() == previousCount
		? "All keys already enabled"
		: "All keys enabled");
}

static
void GivePlayerAllWeapons()
{
	if (!g_developerMode) {
		return;
	}

	syncActivePlayerWeaponFromEngine();
	auto changed = false;
	for (auto& playerWeapon : g_playerWeapons) {
		changed = !playerWeapon.unlocked || changed;
		playerWeapon.unlocked = true;
	}

	if (!hasEquippedPlayerWeapon()) {
		equipFirstUnlockedPlayerWeapon();
	}

	pushEventMessage(changed
		? "All weapons available"
		: "All weapons already available");
}

static
void RefillPlayerAmmo()
{
	if (!g_developerMode) {
		return;
	}

	syncActivePlayerWeaponFromEngine();

	auto changed = false;
	auto hadAmmoWeapon = false;
	for (auto& playerWeapon : g_playerWeapons) {
		if (!playerWeapon.unlocked || !playerWeapon.weapon.usesAmmo()) {
			continue;
		}

		hadAmmoWeapon = true;
		changed = playerWeapon.weapon.refillAmmoToMax() || changed;
	}

	if (g_activePlayerWeaponIndex < g_playerWeapons.size()
		&& g_playerWeapons[g_activePlayerWeaponIndex].unlocked
		&& the3DEngine) {
		the3DEngine->setViewWeapon(
			g_playerWeapons[g_activePlayerWeaponIndex].weapon);
	}

	g_weaponAutoReloadPending = false;
	pushEventMessage(
		hadAmmoWeapon
		? (changed ? "Ammo refilled" : "Ammo already full")
		: "No ammo-based weapon available");
}

static
void RefillPlayerEnergy()
{
	if (!g_developerMode) {
		return;
	}

	const auto previousHealth = g_playerCombatStats.health;
	g_playerCombatStats.health = maxDouble(
		0.0,
		g_playerCombatStats.maxHealth);

	pushEventMessage(
		g_playerCombatStats.health > previousHealth + 0.5
		? "Energy restored"
		: "Energy already full",
		false);
}

static
void JumpToDeveloperLayer(size_t menuIndex)
{
	if (!g_developerMode
		|| menuIndex >= g_developerLayerMenuIds.size()
		|| g_currentProjectPath.empty()) {
		return;
	}

	const auto targetLayerId = g_developerLayerMenuIds[menuIndex];
	if (targetLayerId.empty()) {
		return;
	}

	const auto previousLayerId = g_activeLayerId;
	g_activeLayerId = targetLayerId;
	if (!SwitchToActiveLayer(nullptr, true)) {
		g_activeLayerId = previousLayerId;
		return;
	}

	g_pendingLayerTransition = {};
	g_elevatorShake = {};
	g_layerTransitionArmed = true;
	hideElevatorSelectionPanel();
	hideSavePointPanel();
	if (g_developerLayerMenu != nullptr) {
		CheckMenuRadioItem(
			g_developerLayerMenu,
			ID_DEV_LEVEL_FIRST,
			ID_DEV_LEVEL_LAST,
			ID_DEV_LEVEL_FIRST + static_cast<UINT>(menuIndex),
			MF_BYCOMMAND);
	}
	pushEventMessage("Arrived at " + elevatorLayerDisplayName(targetLayerId));
}

static
void SetBackgroundMusicVolume(int volumePercent)
{
	g_backgroundMusicVolumePercent = clampPercent(volumePercent);
	applyBackgroundMusicState();
}

static
void AdjustBackgroundMusicVolume(int deltaPercent)
{
	SetBackgroundMusicVolume(g_backgroundMusicVolumePercent + deltaPercent);
}

static
void ResetBackgroundMusicVolume()
{
	SetBackgroundMusicVolume(g_backgroundMusicInitialVolumePercent);
}

static
void SetProjectionWindowScale(double scale, bool fitToScreen)
{
	g_projectionWindowFitToScreen = fitToScreen;
	g_projectionWindowScale = fitToScreen
		? projectionWindowFitScaleToScreen()
		: clampProjectionWindowScale(scale);
	resizeWindowForProjectionScale(g_projectionWindowScale);
	updateBackgroundMusicMenu();
}


/* -------------------------------------------------------------------------- */

int APIENTRY WinMain(HINSTANCE hInstance,
	HINSTANCE hPrevInstance,
	LPSTR     lpCmdLine,
	int       nCmdShow)
{
	MSG msg = { 0 };
	const auto options = parseCommandLineOptions(lpCmdLine);
	g_developerMode = options.developerMode;

	if (InitInstance(hInstance, nCmdShow) != S_OK) {
		return FALSE;
	}

	g_hInstance = hInstance;

	g_backgroundMusicEnabled = options.backgroundMusicEnabled;
	g_soundEffectsEnabled = options.soundEffectsEnabled;
	g_playerImmortal = g_developerMode && options.playerImmortal;
	configureDeveloperMenus();
	updateBackgroundMusicMenu();

	const auto loaded = options.projectPath.empty()
		? LoadDefaultWorldIntoEngine()
		: LoadProjectOrWorldIntoEngine(options.projectPath);
	if (!loaded) {
		return FALSE;
	}
	if (options.testTextToSpeech) {
		pushEventMessage("Neural speech test", true);
	}



	g_bActive = TRUE;

	for (;;) {
		if (!g_bActive) {
			const auto gotMessage = GetMessage(&msg, NULL, 0U, 0U);
			if (gotMessage <= 0) {
				break;
			}

			TranslateMessage(&msg);
			DispatchMessage(&msg);
			continue;
		}

		while (PeekMessage(&msg, NULL, 0U, 0U, PM_REMOVE)) {
			if (msg.message == WM_QUIT) {
				return int(msg.wParam);
			}

			TranslateMessage(&msg);
			DispatchMessage(&msg);
		}

		if (g_bActive) {
			using Clock = std::chrono::steady_clock;
			using Ms = std::chrono::duration<double, std::milli>;

			static Clock::time_point lastFrameTick = Clock::now();
			const auto frameTick = Clock::now();
			const double interFrameMs = Ms(frameTick - lastFrameTick).count();
			lastFrameTick = frameTick;
			// Skip the very first (huge) interval so the average settles cleanly.
			if (interFrameMs > 0.0 && interFrameMs < 1000.0) {
				emaUpdate(g_frameStats.frameMs, interFrameMs);
				g_frameStats.fps = g_frameStats.frameMs > 0.0
					? 1000.0 / g_frameStats.frameMs
					: 0.0;
			}

			const auto updateBegin = Clock::now();
			MovePlayer();
			UpdateActors();
			emaUpdate(g_frameStats.updateMs, Ms(Clock::now() - updateBegin).count());

			Render3DEnvironment();
		}
	}

	return int(msg.wParam);
}


/* -------------------------------------------------------------------------- */

static
ATOM WRCstRegisterClass(HINSTANCE hInstance)
{
	WNDCLASSEX wcex;

	wcex.cbSize = sizeof(WNDCLASSEX);

	wcex.style = CS_HREDRAW | CS_VREDRAW;
	wcex.lpfnWndProc = (WNDPROC)WndProc;
	wcex.cbClsExtra = 0;
	wcex.cbWndExtra = 0;
	wcex.hInstance = hInstance;
	wcex.hIcon = LoadIcon(hInstance, (LPCTSTR)IDI_NURCADE);
	wcex.hCursor = LoadCursor(NULL, IDC_ARROW);
	wcex.hbrBackground = 0; //(HBRUSH)(COLOR_WINDOW+1);
	wcex.lpszMenuName = g_FullScreenModeActive ? 0 : (LPCSTR)IDC_NURCADE;
	wcex.lpszClassName = g_szAppWinClass;
	wcex.hIconSm = LoadIcon(wcex.hInstance, (LPCTSTR)IDI_SMALL);

	return RegisterClassEx(&wcex);
}


/* -------------------------------------------------------------------------- */

static
bool SwitchToActiveLayer(
	const SceneLoader::PlayerStart* targetPlayerStart,
	bool preservePlayerStats)
{
	if (g_currentProjectPath.empty()
		|| g_currentWorldPath.empty()
		|| g_activeLayerId.empty()) {
		return false;
	}

	SceneLoader sceneLoader;
	const auto sceneResult =
		sceneLoader.loadFromFile(g_currentProjectPath, g_activeLayerId);
	if (!sceneResult.success) {
		std::string message = "Failed to switch to layer '"
			+ g_activeLayerId + "'.";
		for (const auto& error : sceneResult.errors) {
			message += "\n";
			message += error;
		}

		MessageBox(g_hWnd, message.c_str(), g_szAppTitle, MB_OK | MB_ICONERROR);
		return false;
	}

	const auto previousStats = g_playerCombatStats;
	syncRuntimeActorStates();
	syncRuntimeSpriteStates();
	syncActivePlayerWeaponFromEngine();
	std::map<std::string, std::pair<int, int>> previousWeaponAmmoByFile;
	std::map<std::string, bool> previousWeaponUnlockedByFile;
	std::string previousActiveWeaponFile;
	if (g_activePlayerWeaponIndex < g_playerWeapons.size()) {
		previousActiveWeaponFile =
			g_playerWeapons[g_activePlayerWeaponIndex].file;
	}

	for (const auto& playerWeapon : g_playerWeapons) {
		previousWeaponUnlockedByFile[playerWeapon.file] =
			playerWeapon.unlocked;
		if (playerWeapon.weapon.usesAmmo()) {
			previousWeaponAmmoByFile[playerWeapon.file] = {
				playerWeapon.weapon.ammoInMagazine(),
				playerWeapon.weapon.reserveAmmo()
			};
		}
	}

	auto previousPlayerWeapons = g_playerWeapons;
	const auto previousActivePlayerWeaponIndex = g_activePlayerWeaponIndex;

	auto previousWorldMap = std::move(theWorldMap);
	auto previousEngine = std::move(the3DEngine);

	if (!Setup3DEngine(
		g_currentWorldPath,
		g_currentWorldDir,
		&sceneResult.scene,
		g_currentProjectDir,
		g_activeLayerId,
		targetPlayerStart,
		preservePlayerStats)) {
		theWorldMap = std::move(previousWorldMap);
		the3DEngine = std::move(previousEngine);
		g_playerCombatStats = previousStats;
		g_playerWeapons = std::move(previousPlayerWeapons);
		g_activePlayerWeaponIndex = previousActivePlayerWeaponIndex;
		return false;
	}

	if (preservePlayerStats) {
		g_playerCombatStats = previousStats;
		restorePlayerWeaponUnlockedState(previousWeaponUnlockedByFile);
		restorePlayerWeaponAmmo(previousWeaponAmmoByFile);

		size_t restoredWeaponIndex = g_activePlayerWeaponIndex;
		if (!previousActiveWeaponFile.empty()) {
			for (size_t index = 0; index < g_playerWeapons.size(); ++index) {
				if (g_playerWeapons[index].file == previousActiveWeaponFile) {
					restoredWeaponIndex = index;
					break;
				}
			}
		}

		if (restoredWeaponIndex < g_playerWeapons.size()) {
			if (!activatePlayerWeapon(restoredWeaponIndex, false)) {
				equipFirstUnlockedPlayerWeapon();
			}
		}
	}

	return true;
}


/* -------------------------------------------------------------------------- */

static
bool layerTransitionMatchesCell(
	const LayerTransition& transition,
	int row,
	int column,
	uint8_t blockId) noexcept
{
	if (transition.fromLayer != g_activeLayerId) {
		return false;
	}

	if (transition.hasTriggerCell
		&& (transition.triggerRow != row || transition.triggerColumn != column)) {
		return false;
	}

	uint8_t triggerBlockId = 0;
	return tryParseBlockId(transition.triggerBlockId, triggerBlockId)
		&& triggerBlockId == blockId;
}

static
std::vector<size_t> layerTransitionsForElevatorCell(
	int row,
	int column,
	uint8_t blockId)
{
	std::vector<size_t> matches;
	for (size_t index = 0; index < g_layerTransitions.size(); ++index) {
		if (layerTransitionMatchesCell(
			g_layerTransitions[index],
			row,
			column,
			blockId)) {
			matches.push_back(index);
		}
	}

	return matches;
}

static
void hideElevatorSelectionPanel() noexcept
{
	g_elevatorPanel = {};
	g_elevatorPanelUpWasPressed = false;
	g_elevatorPanelDownWasPressed = false;
	g_elevatorPanelEnterWasPressed = false;
	g_elevatorPanelEscapeWasPressed = false;
}

static
void showElevatorSelectionPanel(
	int row,
	int column,
	uint8_t blockId,
	std::vector<size_t> transitionIndices)
{
	if (transitionIndices.empty()) {
		hideElevatorSelectionPanel();
		return;
	}

	if (g_elevatorPanel.visible
		&& g_elevatorPanel.row == row
		&& g_elevatorPanel.column == column
		&& g_elevatorPanel.blockId == blockId
		&& g_elevatorPanel.transitionIndices == transitionIndices) {
		return;
	}

	g_elevatorPanel.visible = true;
	g_elevatorPanel.row = row;
	g_elevatorPanel.column = column;
	g_elevatorPanel.blockId = blockId;
	g_elevatorPanel.transitionIndices = std::move(transitionIndices);
	g_elevatorPanel.selectedIndex = 0;
	pushEventMessage("Choose destination");
}

static
void beginLayerTransition(
	const LayerTransition& transition,
	int row,
	int column)
{
	if (!transition.requiredKey.empty()
		&& !playerHasKeyId(transition.requiredKey)) {
		pushEventMessage(
			"Access denied: " + transition.requiredKey + " key required",
			true);
		return;
	}

	hideElevatorSelectionPanel();

	g_pendingLayerTransition.active = true;
	g_pendingLayerTransition.targetLayer = transition.toLayer;
	g_pendingLayerTransition.waitSeconds =
		maxDouble(0.0, transition.waitSeconds);
	g_pendingLayerTransition.elapsedSeconds = 0.0;
	g_pendingLayerTransition.targetPlayerStart =
		transition.targetPlayerStart;
	g_pendingLayerTransition.hasTargetPlayerStart =
		transition.hasTargetPlayerStart;
	g_pendingLayerTransition.triggerRow = row;
	g_pendingLayerTransition.triggerColumn = column;

	g_layerTransitionArmed = false;

	g_elevatorShake.active = true;
	g_elevatorShake.elapsedSeconds = 0.0;
	g_elevatorShake.totalSeconds =
		g_pendingLayerTransition.waitSeconds + 1.1;
	g_elevatorShake.hasBaseline = false;
	playDoorOpeningEffectAt(row, column);
}

static
void updateElevatorSelectionPanelInput()
{
	if (!g_elevatorPanel.visible || g_elevatorPanel.transitionIndices.empty()) {
		hideElevatorSelectionPanel();
		return;
	}

	const bool upPressed = pollKey(VK_UP) || pollKey('W');
	const bool downPressed = pollKey(VK_DOWN) || pollKey('S');
	const bool enterPressed = pollKey(VK_RETURN);
	const bool escapePressed = pollKey(VK_ESCAPE);

	if (upPressed && !g_elevatorPanelUpWasPressed) {
		if (g_elevatorPanel.selectedIndex == 0) {
			g_elevatorPanel.selectedIndex =
				g_elevatorPanel.transitionIndices.size() - 1;
		}
		else {
			--g_elevatorPanel.selectedIndex;
		}
	}

	if (downPressed && !g_elevatorPanelDownWasPressed) {
		g_elevatorPanel.selectedIndex =
			(g_elevatorPanel.selectedIndex + 1)
			% g_elevatorPanel.transitionIndices.size();
	}

	if (escapePressed && !g_elevatorPanelEscapeWasPressed) {
		hideElevatorSelectionPanel();
		g_layerTransitionArmed = false;
		pushEventMessage("Destination selection cancelled");
	}
	else if (enterPressed && !g_elevatorPanelEnterWasPressed) {
		const auto optionIndex = g_elevatorPanel.selectedIndex;
		if (optionIndex < g_elevatorPanel.transitionIndices.size()) {
			const auto transitionIndex = g_elevatorPanel.transitionIndices[optionIndex];
			if (transitionIndex < g_layerTransitions.size()) {
				beginLayerTransition(
					g_layerTransitions[transitionIndex],
					g_elevatorPanel.row,
					g_elevatorPanel.column);
			}
		}
	}

	g_elevatorPanelUpWasPressed = upPressed;
	g_elevatorPanelDownWasPressed = downPressed;
	g_elevatorPanelEnterWasPressed = enterPressed;
	g_elevatorPanelEscapeWasPressed = escapePressed;
}

/* -------------------------------------------------------------------------- */

static
void UpdateLayerTransition(double deltaSeconds)
{
	if (!the3DEngine || !theWorldMap) {
		return;
	}

	auto& player = the3DEngine->player();

	// Apply elevator tremor: wobble vertical slope and horizontal projection center.
	if (g_elevatorShake.active) {
		if (!g_elevatorShake.hasBaseline) {
			g_elevatorShake.baseSlope = player.getSlope();
			g_elevatorShake.baseCenterProj = player.getCenterProj();
			g_elevatorShake.hasBaseline = true;
		}

		g_elevatorShake.elapsedSeconds += deltaSeconds;
		const auto total = g_elevatorShake.totalSeconds > 0.01
			? g_elevatorShake.totalSeconds
			: 0.01;
		const auto progress = g_elevatorShake.elapsedSeconds / total;

		if (progress >= 1.0) {
			player.setSlope(g_elevatorShake.baseSlope);
			player.setCenterProj(g_elevatorShake.baseCenterProj);
			g_elevatorShake = {};
		}
		else {
			const auto easing = 1.0 - progress;
			const auto t = g_elevatorShake.elapsedSeconds;
			const auto verticalAmp = 14.0 * easing;
			const auto horizontalAmp = 0.06 * easing;
			const auto slopeOffset =
				std::sin(t * 36.0) * verticalAmp
				+ std::sin(t * 22.0 + 0.7) * verticalAmp * 0.45;
			const auto centerOffset =
				std::sin(t * 19.0 + 0.3) * horizontalAmp;
			player.setSlope(
				g_elevatorShake.baseSlope
				+ static_cast<int>(std::round(slopeOffset)));
			player.setCenterProj(
				g_elevatorShake.baseCenterProj + centerOffset);
		}
	}

	if (g_layerTransitions.empty() || g_activeLayerId.empty()) {
		return;
	}

	if (g_pendingLayerTransition.active) {
		if (g_pendingLayerTransition.triggerRow >= 0
			&& g_pendingLayerTransition.triggerColumn >= 0) {
			theWorldMap->forceDoorClosingAt(
				g_pendingLayerTransition.triggerRow,
				g_pendingLayerTransition.triggerColumn,
				deltaSeconds);
		}

		g_pendingLayerTransition.elapsedSeconds += deltaSeconds;
		if (g_pendingLayerTransition.elapsedSeconds
			< g_pendingLayerTransition.waitSeconds) {
			return;
		}

		const auto targetLayer = g_pendingLayerTransition.targetLayer;
		const auto targetPlayerStart = g_pendingLayerTransition.targetPlayerStart;
		const auto hasTargetPlayerStart =
			g_pendingLayerTransition.hasTargetPlayerStart;

		g_activeLayerId = targetLayer;
		g_pendingLayerTransition = {};
		g_layerTransitionArmed = false;
		const auto switched = SwitchToActiveLayer(
			hasTargetPlayerStart ? &targetPlayerStart : nullptr,
			true);
		if (switched && hasTargetPlayerStart && theWorldMap != nullptr) {
			const auto targetColumn =
				static_cast<int>(std::floor(targetPlayerStart.xCell));
			const auto targetRow =
				static_cast<int>(std::floor(targetPlayerStart.yCell));
			theWorldMap->forceDoorOpenAt(targetRow, targetColumn);
			playDoorOpeningEffectAt(targetRow, targetColumn);
		}
		if (g_elevatorShake.active) {
			g_elevatorShake.hasBaseline = false;
		}
		return;
	}

	const auto row = player.getRow(theWorldMap->getCellDy());
	const auto column = player.getCol(theWorldMap->getCellDx());
	if (row < 0
		|| column < 0
		|| row >= theWorldMap->getRowCount()
		|| column >= theWorldMap->getColCount()) {
		return;
	}

	const auto currentBlockId = theWorldMap->blockIdAt(row, column);
	auto availableTransitions =
		layerTransitionsForElevatorCell(row, column, currentBlockId);
	const auto playerOnAnyTriggerOfActiveLayer =
		!availableTransitions.empty();

	if (!playerOnAnyTriggerOfActiveLayer) {
		if (g_elevatorPanel.visible) {
			hideElevatorSelectionPanel();
		}
		g_layerTransitionArmed = true;
	}

	if (!g_layerTransitionArmed || g_elevatorPanel.visible) {
		return;
	}

	if (availableTransitions.empty()) {
		return;
	}

	const auto* block = theWorldMap->blockAtCell(row, column);
	if (block != nullptr
		&& block->door.enabled
		&& !theWorldMap->isDoorOpenAt(row, column)) {
		return;
	}

	showElevatorSelectionPanel(
		row,
		column,
		currentBlockId,
		std::move(availableTransitions));
}


/* -------------------------------------------------------------------------- */

static
void UpdateActors()
{
	if (!the3DEngine || !theWorldMap) {
		g_lastActorUpdateMs = GetTickCount64();
		return;
	}

	const auto now = GetTickCount64();
	if (g_lastActorUpdateMs == 0) {
		g_lastActorUpdateMs = now;
		return;
	}

	auto deltaSeconds =
		static_cast<double>(now - g_lastActorUpdateMs) / 1000.0;
	g_lastActorUpdateMs = now;

	if (deltaSeconds > 0.1) {
		deltaSeconds = 0.1;
	}

	std::vector<size_t> actorSpriteIndices;
	std::vector<WorldMap::Point2d> actorPositions;
	actorSpriteIndices.reserve(g_spriteActors.size());
	actorPositions.reserve(g_spriteActors.size());
	for (const auto& actor : g_spriteActors) {
		actorSpriteIndices.push_back(actor.spriteIndex);
		const auto* sprite = the3DEngine->sprite(actor.spriteIndex);
		if (!actor.dead && sprite != nullptr && sprite->visible) {
			actorPositions.emplace_back(sprite->x, sprite->y);
		}
	}

	std::vector<WorldMap::DoorEvent> doorEvents;
	theWorldMap->advanceDynamicTextures(deltaSeconds);
	theWorldMap->updateDoors(
		the3DEngine->player().getX(),
		the3DEngine->player().getY(),
		actorPositions,
		deltaSeconds,
		&doorEvents);
	playDoorOpeningEffects(doorEvents);

	updatePlayerPropViewLift();
	UpdateLayerTransition(deltaSeconds);
	updateSavePointInteraction();
	updatePlayerPickups();
	updatePlayerLifeState(deltaSeconds);
	updateDamageFlash(deltaSeconds);
	if (g_playerLifeState == PlayerLifeState::Alive
		&& !g_gameCompleted
		&& !g_gameOver) {
		g_missionElapsedSeconds += deltaSeconds;
	}

	if (g_playerLifeState == PlayerLifeState::Alive
		&& !g_gameCompleted
		&& !g_spriteActors.empty()) {
		updateActorRangedAttacks(deltaSeconds);
		g_actorSystem.update(
			*the3DEngine,
			*theWorldMap,
			g_spriteActors,
			deltaSeconds);
		updateActorMeleeAttacks();
		syncRuntimeActorStates();
	}

	the3DEngine->advanceSpriteAnimations(deltaSeconds, actorSpriteIndices);
	updateRuntimeExplosions(deltaSeconds);
	the3DEngine->advanceViewWeapon(deltaSeconds, g_playerMovingThisFrame);
	updatePendingViewWeaponReload();
	updateGameCompletionState();
	updateCompletionSummary(deltaSeconds);
}


/* -------------------------------------------------------------------------- */

static
void MovePlayer()
{
	g_playerMovingThisFrame = false;

	if (g_playerLifeState != PlayerLifeState::Alive || g_gameCompleted) {
		return;
	}

	if (g_pendingLayerTransition.active) {
		return;
	}

	if (g_elevatorPanel.visible) {
		updateElevatorSelectionPanelInput();
		return;
	}

	if (g_savePointPanel.visible) {
		updateSavePointPanelInput();
		return;
	}

	// Progressive-turn state, retained across frames.
	static ULONGLONG turnLastTickMs = 0;
	static int turnDir = 0;                 // -1 left, +1 right, 0 idle
	static double turnRateDegPerSec = 0.0;  // current ramped rate
	static double turnAccumUnits = 0.0;     // fractional angle-unit carryover

	const ULONGLONG turnNowMs = GetTickCount64();
	double turnDt = 0.0;
	if (turnLastTickMs != 0) {
		turnDt = static_cast<double>(turnNowMs - turnLastTickMs) / 1000.0;
		if (turnDt > 0.1) {
			turnDt = 0.1;  // clamp long stalls so a hitch can't fling the view around
		}
	}
	turnLastTickMs = turnNowMs;

	const bool shift_pressed = pollKey(VK_LSHIFT) || pollKey(VK_RSHIFT) || pollKey(VK_SHIFT);
	int speedFact = SPEED_FACTOR;

	if (shift_pressed) {
		speedFact = RUNNING_SPEED_FACTOR;
		// Strafing takes over Left/Right; reset the turn ramp so it starts fresh on release.
		turnDir = 0;
		turnRateDegPerSec = 0.0;
		turnAccumUnits = 0.0;
		if (the3DEngine && theWorldMap && pollKey(VK_LEFT)) {
			g_current_cell_of_player =
				movePlayerRespectingDestructibleProps(
					KEYBSTEP,
					-the3DEngine->player().deg90());
			g_playerMovingThisFrame = true;
		}
		else if (the3DEngine && theWorldMap && pollKey(VK_RIGHT)) {
			g_current_cell_of_player =
				movePlayerRespectingDestructibleProps(
					-KEYBSTEP,
					-the3DEngine->player().deg90());
			g_playerMovingThisFrame = true;
		}
	}
	else {
		int requestedTurnDir = 0;
		if (the3DEngine && pollKey(VK_LEFT)) {
			requestedTurnDir = -1;
		}
		else if (the3DEngine && pollKey(VK_RIGHT)) {
			requestedTurnDir = 1;
		}

		// Turn feel, overridable per-world (falls back to the built-in defaults).
		double turnBaseDegPerSec = kTurnBaseDegPerSec;
		double turnMaxDegPerSec = kTurnMaxDegPerSec;
		double turnAccelDegPerSec2 = kTurnAccelDegPerSec2;
		if (theWorldMap) {
			turnBaseDegPerSec = theWorldMap->getPlayerTurnBaseDegPerSec();
			turnMaxDegPerSec = theWorldMap->getPlayerTurnMaxDegPerSec();
			turnAccelDegPerSec2 = theWorldMap->getPlayerTurnAccelDegPerSecSq();
		}

		if (requestedTurnDir != 0) {
			if (requestedTurnDir != turnDir) {
				// New press or reversed direction: start from the base rate.
				turnRateDegPerSec = turnBaseDegPerSec;
				turnAccumUnits = 0.0;
			}
			else {
				turnRateDegPerSec += turnAccelDegPerSec2 * turnDt;
				if (turnRateDegPerSec > turnMaxDegPerSec) {
					turnRateDegPerSec = turnMaxDegPerSec;
				}
			}
			turnDir = requestedTurnDir;

			const double unitsPerDegree =
				static_cast<double>(the3DEngine->player().deg360()) / 360.0;
			turnAccumUnits += turnRateDegPerSec * turnDt * unitsPerDegree;

			const int stepUnits = static_cast<int>(turnAccumUnits);
			if (stepUnits > 0) {
				turnAccumUnits -= stepUnits;
				the3DEngine->player().rotate(static_cast<double>(turnDir * stepUnits));
			}
		}
		else {
			turnDir = 0;
			turnRateDegPerSec = 0.0;
			turnAccumUnits = 0.0;
		}
	}

	bool move_up_down = false;

	if (the3DEngine && theWorldMap && pollKey(VK_UP)) {
		g_current_cell_of_player =
			movePlayerRespectingDestructibleProps(KEYBSTEP * speedFact);

		move_up_down = true;
		g_playerMovingThisFrame = true;
	}
	else if (the3DEngine && theWorldMap && pollKey(VK_DOWN)) {
		g_current_cell_of_player =
			movePlayerRespectingDestructibleProps(-KEYBSTEP * speedFact);

		move_up_down = true;
		g_playerMovingThisFrame = true;
	}

	if (the3DEngine && pollKey(VK_PRIOR)) {
		the3DEngine->player().setSlope(
			the3DEngine->player().getSlope() + KEYBSTEP);
	}
	else if (the3DEngine && pollKey(VK_NEXT)) {
		the3DEngine->player().setSlope(
			the3DEngine->player().getSlope() - KEYBSTEP);
	}

	if (the3DEngine && pollKey(VK_END)) {
		the3DEngine->player().setCenterProj(double(0.90));
	}
	else if (the3DEngine && pollKey(VK_HOME)) {
		the3DEngine->player().setCenterProj(double(0.10));
	}

	for (size_t weaponIndex = 0;
		weaponIndex < g_playerWeapons.size() && weaponIndex < 9;
		++weaponIndex) {
		const bool switchPressed = pollKey('1' + static_cast<int>(weaponIndex));
		if (switchPressed && !g_weaponSwitchWasPressed[weaponIndex]) {
			activatePlayerWeapon(weaponIndex);
		}

		g_weaponSwitchWasPressed[weaponIndex] = switchPressed;
	}

	if (the3DEngine && the3DEngine->viewWeapon()) {
		const bool firePressed = pollKey(VK_SPACE) || pollKey(VK_LBUTTON);
		const bool reloadPressed = pollKey('R');
		auto* weapon = the3DEngine->viewWeapon();

		const auto automaticFire = weapon->automaticFire();
		const auto shouldFire = automaticFire
			? firePressed && weapon->fireEventReady()
			: firePressed && !g_weaponFireWasPressed;
		if (shouldFire) {
			const auto weaponReady = weapon->activeAnimationName() == "idle"
				|| (automaticFire && weapon->activeAnimationName() == "fire");
			if (weaponReady && weapon->canFire() && weapon->consumeRound()) {
				weapon->restartAnimationOrFallback("fire", "idle");
				weapon->markFireEventStarted();
				if (weapon->fireSoundReady()) {
					playViewWeaponFireSound(*weapon);
					weapon->markFireSoundStarted();
				}
				alertActorsFromWeaponNoise(*weapon);
				applyViewWeaponDamage();
				if (weapon->needsReload()) {
					g_weaponAutoReloadPending = true;
				}
			}
			else if (weaponReady) {
				startViewWeaponReload(*weapon);
			}
		}

		if (reloadPressed && !g_weaponReloadWasPressed) {
			startViewWeaponReload(*weapon);
		}

		g_weaponFireWasPressed = firePressed;
		g_weaponReloadWasPressed = reloadPressed;
	}
}


/* -------------------------------------------------------------------------- */

static
void Render3DEnvironment() {
	RECT rt{};
	GetClientRect(g_hWnd, &rt);

	const auto clientWidth = static_cast<int>(rt.right - rt.left);
	const auto clientHeight = static_cast<int>(rt.bottom - rt.top);
	const auto desiredRenderWidth = static_cast<int>(
		std::round(RENDER_X_RES * g_projectionWindowScale));
	const auto desiredRenderHeight = static_cast<int>(
		std::round(RENDER_Y_RES * g_projectionWindowScale));
	// Bottom teletype panel height is configured per-world (0 when the log is disabled).
	int bottomPanelHeight = 0;
	if (theWorldMap && theWorldMap->messageLog().enabled) {
		const auto lines = clampInt(theWorldMap->messageLog().maxLines, 1, 4);
		bottomPanelHeight = lines * EVENT_LOG_LINE_HEIGHT + 16;
	}
	const auto availableRenderWidth =
		(std::max)(1, clientWidth - HUD_PANEL_X_RES);
	const auto availableRenderHeight =
		(std::max)(1, clientHeight - bottomPanelHeight);
	auto renderWidth = (std::min)(availableRenderWidth, desiredRenderWidth);
	auto renderHeight = (std::min)(availableRenderHeight, desiredRenderHeight);
	if (renderWidth * RENDER_Y_RES > renderHeight * RENDER_X_RES) {
		renderWidth = (std::max)(1, renderHeight * RENDER_X_RES / RENDER_Y_RES);
	}
	else {
		renderHeight = (std::max)(1, renderWidth * RENDER_Y_RES / RENDER_X_RES);
	}
	const auto clientVideoPosX = 0;
	const auto clientVideoPosY = 0;

	if (the3DEngine && theWorldMap && renderWidth > 0 && renderHeight > 0) {
		// Reserve a 1px ring at the edge of the viewport for the static grey border so
		// the per-frame projection blit never overwrites it (which caused flicker).
		// presentFrameBuffer blits with a negative DestHeight (vertical mirror for the
		// bottom-up DIB); that bottom destination edge is inclusive, so the blit reaches
		// one extra device row at the bottom. Reserve 2px there, 1px on the other sides.
		const bool inset = (renderWidth > 3 && renderHeight > 3);
		const int insetLeft = inset ? 1 : 0;
		const int insetTop = inset ? 1 : 0;
		const int insetRight = inset ? 1 : 0;
		const int insetBottom = inset ? 2 : 0;
		const int innerWidth = renderWidth - insetLeft - insetRight;
		const int innerHeight = renderHeight - insetTop - insetBottom;

		using Clock = std::chrono::steady_clock;
		using Ms = std::chrono::duration<double, std::milli>;

		the3DEngine->renderToFrameBuffer(*theWorldMap, innerWidth, innerHeight);
		emaUpdate(g_frameStats.renderMs, the3DEngine->lastRenderProfile().totalMs);

		const int srcWidth = the3DEngine->player().getXProjRes();
		const int srcHeight = the3DEngine->player().getYProjRes();

		if (g_presenter.ready() && g_presenter.beginFrame()) {
			// Hardware path: GPU-scaled 3D frame + GDI HUD via Direct2D interop.
			const auto presentBegin = Clock::now();
			const RECT viewportRect{
				clientVideoPosX + insetLeft,
				clientVideoPosY + insetTop,
				clientVideoPosX + insetLeft + innerWidth,
				clientVideoPosY + insetTop + innerHeight
			};
			g_presenter.draw3D(
				the3DEngine->frameBuffer(), viewportRect, srcWidth, srcHeight);
			emaUpdate(g_frameStats.presentMs, Ms(Clock::now() - presentBegin).count());

			const auto hudBegin = Clock::now();
			if (HDC hdc = g_presenter.beginGdi()) {
				drawDamageFlashOverlay(
					hdc, clientVideoPosX + insetLeft, clientVideoPosY + insetTop,
					innerWidth, innerHeight);
				drawPlayerDeathOverlay(
					hdc, clientVideoPosX + insetLeft, clientVideoPosY + insetTop,
					innerWidth, innerHeight);
				drawRuntimeHud(
					hdc, clientVideoPosX, clientVideoPosY,
					clientWidth, clientHeight, renderWidth, renderHeight);
				g_presenter.endGdi();
			}
			emaUpdate(g_frameStats.hudMs, Ms(Clock::now() - hudBegin).count());

			g_presenter.endFrame();
		}
		else {
			// Legacy fallback: DirectDraw primary + GDI StretchDIBits / HUD.
			POINT screenClientOrigin{ 0, 0 };
			ClientToScreen(g_hWnd, &screenClientOrigin);
			const auto screenVideoPosX = screenClientOrigin.x;
			const auto screenVideoPosY = screenClientOrigin.y;

			const auto presentBegin = Clock::now();
			presentFrameBuffer(
				screenVideoPosX + insetLeft,
				screenVideoPosY + insetTop,
				the3DEngine->frameBuffer(),
				srcWidth,
				srcHeight);
			emaUpdate(g_frameStats.presentMs, Ms(Clock::now() - presentBegin).count());

			const auto hudBegin = Clock::now();
			DdxDevice::Ctx dctx(DdxDevice::getInstance());
			if (HDC hdc = dctx.getDc()) {
				drawDamageFlashOverlay(
					hdc, screenVideoPosX + insetLeft, screenVideoPosY + insetTop,
					innerWidth, innerHeight);
				drawPlayerDeathOverlay(
					hdc, screenVideoPosX + insetLeft, screenVideoPosY + insetTop,
					innerWidth, innerHeight);
				drawRuntimeHud(
					hdc, screenVideoPosX, screenVideoPosY,
					clientWidth, clientHeight, renderWidth, renderHeight);
			}
			emaUpdate(g_frameStats.hudMs, Ms(Clock::now() - hudBegin).count());
		}
	}
}


/* -------------------------------------------------------------------------- */

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
	int wmId, wmEvent;
	PAINTSTRUCT ps;
	HDC videoHdc, hdc = 0;

	switch (message) {
	case WM_COMMAND:
		wmId = LOWORD(wParam);
		wmEvent = HIWORD(wParam);
		if (g_developerMode
			&& wmId >= ID_DEV_LEVEL_FIRST
			&& wmId <= ID_DEV_LEVEL_LAST) {
			JumpToDeveloperLayer(
				static_cast<size_t>(wmId - ID_DEV_LEVEL_FIRST));
			return 0L;
		}
		// Parse the menu selections:
		switch (wmId)
		{
		case ID_FILE_INFO: {
			char info[256] = { 0 };
			sprintf(
				info,
				"X_RES = %i\r\n"
				"Y_RES = %i\r\n"
				"RENDER_X_RES = %i\r\n"
				"RENDER_Y_RES = %i\r\n"
				"PROJ_X_RES = %i\r\n"
				"PROJ_Y_RES = %i\r\n"
				"PROJECTION_WINDOW_SCALE = %.2f\r\n"
				"VISUAL_DEGREE = %i\r\n"
				"Direct Draw 7 MODE\r\n"
				, X_RES, Y_RES, RENDER_X_RES, RENDER_Y_RES, PROJ_X_RES, PROJ_Y_RES,
				g_projectionWindowScale, VISUAL_DEGREE
			);
			MessageBox(hWnd, info, g_szAppTitle, 0);
		}
						 break;

		case ID_FILE_OPEN_PROJECT:
			OpenProjectFromMenu(hWnd);
			break;

		case ID_AUDIO_BACKGROUND_MUSIC:
			ToggleBackgroundMusic();
			break;

		case ID_AUDIO_SOUND_EFFECTS:
			ToggleSoundEffects();
			break;

		case ID_AUDIO_EVENT_SPEECH:
			ToggleEventSpeech();
			break;

		case ID_AUDIO_VOLUME_DOWN:
			AdjustBackgroundMusicVolume(-5);
			break;

		case ID_AUDIO_VOLUME_UP:
			AdjustBackgroundMusicVolume(5);
			break;

		case ID_AUDIO_VOLUME_RESET:
			ResetBackgroundMusicVolume();
			break;

		case ID_VIEW_PROJECTION_75:
			SetProjectionWindowScale(0.75, false);
			break;

		case ID_VIEW_PROJECTION_90:
			SetProjectionWindowScale(0.90, false);
			break;

		case ID_VIEW_PROJECTION_100:
			SetProjectionWindowScale(1.00, false);
			break;

		case ID_VIEW_PROJECTION_125:
			SetProjectionWindowScale(1.25, false);
			break;

		case ID_VIEW_PROJECTION_150:
			SetProjectionWindowScale(1.50, false);
			break;

		case ID_VIEW_PROJECTION_200:
			SetProjectionWindowScale(2.00, false);
			break;

		case ID_VIEW_PROJECTION_FIT_SCREEN:
			SetProjectionWindowScale(1.00, true);
			break;

		case ID_GAME_IMMORTAL:
			TogglePlayerImmortal();
			break;

		case ID_GAME_GIVE_ALL_KEYS:
			GivePlayerAllKeys();
			break;

		case ID_GAME_GIVE_ALL_WEAPONS:
			GivePlayerAllWeapons();
			break;

		case ID_GAME_REFILL_AMMO:
			RefillPlayerAmmo();
			break;

		case ID_GAME_REFILL_ENERGY:
			RefillPlayerEnergy();
			break;

		case IDM_ABOUT:
		case ID_FILE_ABOUT:
			DialogBox(g_hInstance, (LPCTSTR)IDD_ABOUTBOX, hWnd, (DLGPROC)About);
			break;

		case IDM_EXIT:
			SendMessage(hWnd, WM_CLOSE, 0, 0);
			break;

		default:
			return DefWindowProc(hWnd, message, wParam, lParam);
		}
		break;

	case WM_SIZE:
		g_presenter.resize(LOWORD(lParam), HIWORD(lParam));
		break;

	case WM_CLOSE:
		if (MessageBoxA(
			hWnd,
			"Exit the game?\nUnsaved progress will be lost.",
			"Confirm exit",
			MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) == IDYES) {
			DestroyWindow(hWnd);
		}
		return 0L;

	case WM_PAINT:
		videoHdc = BeginPaint(hWnd, &ps);
		EndPaint(hWnd, &ps);
		break;

	case WM_ACTIVATE:
		// Pause if minimized
		g_bActive = !((BOOL)HIWORD(wParam));
		return 0L;

	case WM_CHAR:
		if (g_completionSummary.enteringName) {
			const auto character = static_cast<unsigned int>(wParam);
			if (character == '\r') {
				submitCompletionLeaderboardName();
			}
			else if (character == '\b') {
				if (!g_completionSummary.playerName.empty()) {
					g_completionSummary.playerName.pop_back();
				}
			}
			else if (character >= 32 && character <= 126
				&& g_completionSummary.playerName.size() < 16) {
				g_completionSummary.playerName.push_back(
					static_cast<char>(character));
			}
			return 0L;
		}
		break;

	case WM_KEYDOWN:
		// Handle any non-accelerated key commands
		if (g_completionSummary.enteringName && wParam == VK_ESCAPE) {
			g_completionSummary.playerName = "PLAYER";
			submitCompletionLeaderboardName();
			return 0L;
		}

		if (g_completionSummary.awaitingRestart) {
			if (wParam == 'Y') {
				RestartGameFromBeginning();
				return 0L;
			}
			if (wParam == 'N') {
				SendMessage(hWnd, WM_CLOSE, 0, 0);
				return 0L;
			}
		}

		if (g_elevatorPanel.visible && wParam == VK_ESCAPE) {
			hideElevatorSelectionPanel();
			g_layerTransitionArmed = false;
			pushEventMessage("Destination selection cancelled");
			return 0L;
		}

		if (g_savePointPanel.visible && wParam == VK_ESCAPE) {
			hideSavePointPanel();
			pushEventMessage("Recovery cancelled");
			return 0L;
		}

		if (g_gameOver && wParam == VK_RETURN) {
			RestartGameFromBeginning();
			return 0L;
		}
		switch (wParam) {
		case VK_F6:
			g_showPerfHud = !g_showPerfHud;
			return 0L;

		case VK_F7:
			TogglePlayerImmortal();
			return 0L;

		case VK_F8:
			ToggleSoundEffects();
			return 0L;

		case VK_F9:
			ToggleBackgroundMusic();
			return 0L;

		case VK_F10:
			AdjustBackgroundMusicVolume(-5);
			return 0L;

		case VK_F11:
			AdjustBackgroundMusicVolume(5);
			return 0L;

		case VK_ESCAPE:
		case VK_F12:
			SendMessage(hWnd, WM_CLOSE, 0, 0);
			return 0L;
		default:
			break;
		}
		break;

	case WM_DESTROY:
		//delete theJoystick;
		theWorldMap.reset();
		the3DEngine.reset();
		g_backgroundMusicPlayer.stop();
		DdxDevice::getInstance().releaseObjects();
		//ReleaseAllObjects();
		PostQuitMessage(0);
		break;

	default:
		return DefWindowProc(hWnd, message, wParam, lParam);
	}
	return 0;
}


/* -------------------------------------------------------------------------- */

LRESULT CALLBACK About(HWND hDlg, UINT message, WPARAM wParam, LPARAM lParam)
{
	switch (message) {
	case WM_INITDIALOG:
		SetDlgItemTextA(hDlg, IDC_ABOUT_TEXT, kAboutText);
		return TRUE;

	case WM_COMMAND:
		if (LOWORD(wParam) == IDOK || LOWORD(wParam) == IDCANCEL) {
			EndDialog(hDlg, LOWORD(wParam));
			return TRUE;
		}
		break;
	}
	return FALSE;
}


/* -------------------------------------------------------------------------- */

static void DbgTrace(HWND hWnd, LPCTSTR szError, ...)
{
	char szBuff[256];
	va_list vl;

	va_start(vl, szError);
	vsprintf(szBuff, szError, vl);
	//ReleaseAllObjects();

	DdxDevice::getInstance().releaseObjects();
	MessageBox(hWnd, szBuff, g_szAppTitle, MB_OK);
	DestroyWindow(hWnd);
	va_end(vl);
}


/* -------------------------------------------------------------------------- */

static HRESULT InitInstance(HINSTANCE hInstance, int nCmdShow)
{
	WRCstRegisterClass(hInstance);

	const DWORD exStyle = WS_EX_TOPMOST;
	const DWORD windowStyle = g_FullScreenModeActive
		? WS_POPUP
		: WS_POPUPWINDOW | WS_CAPTION | WS_BORDER;
	RECT windowRect{
		0,
		0,
		static_cast<LONG>(std::round(RENDER_X_RES * g_projectionWindowScale)) + HUD_PANEL_X_RES,
		static_cast<LONG>(std::round(RENDER_Y_RES * g_projectionWindowScale)) + HUD_PANEL_Y_RES
	};
	if (!g_FullScreenModeActive) {
		AdjustWindowRectEx(&windowRect, windowStyle, TRUE, exStyle);
	}
	const auto windowWidth = windowRect.right - windowRect.left;
	const auto windowHeight = windowRect.bottom - windowRect.top;

	// Create a window
	HWND hWnd = CreateWindowEx(
		exStyle,
		g_szAppWinClass,
		g_szAppTitle,
		windowStyle,
		0,
		0,
		windowWidth,
		windowHeight,
		NULL,
		NULL,
		hInstance,
		NULL
	);

	if (!hWnd) return FALSE;

	ShowWindow(hWnd, nCmdShow);
	UpdateWindow(hWnd);
	SetFocus(hWnd);
	if (g_FullScreenModeActive) ShowCursor(FALSE);

	auto err = DdxDevice::getInstance().init(hWnd, g_FullScreenModeActive, X_RES, Y_RES);

	if (err != DdxDevice::error_t::Success) {
		DbgTrace(hWnd, "DdxDevice initialization failed");
		DdxDevice::getInstance().releaseObjects();
		DestroyWindow(hWnd);
	}

	g_hWnd = hWnd;

	// Prefer the hardware (Direct2D) presenter in windowed mode; fullscreen keeps
	// the DirectDraw-exclusive path. Failure is non-fatal (legacy path is used).
	if (!g_FullScreenModeActive) {
		g_presenter.init(hWnd);
	}

	updateBackgroundMusicMenu();
	return S_OK;
}


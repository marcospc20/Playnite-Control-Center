using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SDL2;
using System.Windows.Threading;
using System.ComponentModel;

namespace PlayniteControlCenter
{
    public class PlayniteControlCenter : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private OverlaySettings settings;
        private GameOverlayData GameOverlayData;
        private OverlayWindow overlayWindow;
        private IPlayniteAPI playniteAPI;
        private GlobalKeyboardHook keyboardHook;

        public override Guid Id { get; } = Guid.Parse("E6DE5B83-EA29-4207-812D-6C3A3AA26CFC");

        private DateTime gameStarted;

        // SuccessStory integration
        private bool isSuccessStoryAvailable = false;
        private Guid successStoryId = Guid.Parse("cebe6d32-8c46-4459-b993-5a5189d60788"); // SuccessStory plugin ID

        public PlayniteControlCenter(IPlayniteAPI api) : base(api)
        {
            playniteAPI = api;
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            logger.Info("Starting Overlay Extension...");

            // Check if SuccessStory is installed
            CheckSuccessStoryAvailability();

            // Initialize overlay window com IPlayniteAPI
            overlayWindow = new OverlayWindow(Settings, playniteAPI);
            overlayWindow.Hide();

            // Set up show Playnite handler
            overlayWindow.OnShowPlayniteRequested += ShowPlaynite;

            // Initialize global keyboard hook
            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyPressed += OnKeyPressed;

            InitializeController();
            if (controllerTimerFast != null)
            {
                controllerTimerFast.Stop(); // Disable fast polling until game starts
            }
        }

        public void ReloadOverlay(OverlaySettings settings)
        {
            overlayWindow.Close(); // Close old window
            overlayWindow = new OverlayWindow(settings != null ? settings : Settings, playniteAPI); // Open new one with IPlayniteAPI
            overlayWindow.Hide();
            if (GameOverlayData != null)
            {
                overlayWindow.UpdateGameOverlay(GameOverlayData);
            }

            // Set up show Playnite handler again
            overlayWindow.OnShowPlayniteRequested += ShowPlaynite;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            logger.Info("Stopping Overlay...");

            // Cleanup resources
            overlayWindow?.Close();
            keyboardHook?.Dispose();
            if (sdlInitialized)
            {
                SDL.SDL_Quit();
                sdlInitialized = false;
            }
            CloseController();
        }

        private void OnKeyPressed(Keys key, bool altPressed)
        {
            // Alt + ` (backtick) to toggle overlay
            if (altPressed && key == Keys.Oem3)
            {
                var runningGame = playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);
                if (runningGame != null)
                {
                    if (overlayWindow.IsVisible)
                        overlayWindow.Hide();
                    else
                        ShowGameOverlay(runningGame);
                }
                else
                {
                    ShowPlaynite();
                }
            }

            // Escape to hide overlay
            if (key == Keys.Escape && overlayWindow.IsVisible)
            {
                overlayWindow.Hide();
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            try
            {
                gameStarted = DateTime.Now;
                var gameOverlayData = CreateGameOverlayData(args.Game, args.StartedProcessId, gameStarted);
                GameOverlayData = gameOverlayData;
                overlayWindow.UpdateGameOverlay(gameOverlayData);
                if (controllerTimerFast != null)
                {
                    controllerTimerFast.Start(); // Start fast timer
                }
                if (controllerTimerSlow != null)
                {
                    controllerTimerSlow.Stop(); // Disable slow polling
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error preparing game overlay data: {ex}");
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            overlayWindow.UpdateGameOverlay(null);
            GameOverlayData = null;
            if (controllerTimerFast != null)
            {
                controllerTimerFast.Stop(); // Stop fast polling
            }
            if (controllerTimerSlow != null)
            {
                controllerTimerSlow.Start(); // Start slow polling
            }
        }

        private void ShowGameOverlay(Game game)
        {
            if (game == null) return;

            var gameOverlayData = CreateGameOverlayData(game, FindRunningGameProcess(game)?.Id, gameStarted);
            overlayWindow.UpdateGameOverlay(gameOverlayData);
            overlayWindow.ShowOverlay();
        }

        private GameOverlayData CreateGameOverlayData(Game game, int? processId, DateTime startTime)
        {
            if (game == null) return null;

            var achievements = GetGameAchievements(game);

            return new GameOverlayData
            {
                GameId = game.Id, // Adicionado para identificar o jogo no Playnite
                GameName = game.Name,
                ProcessId = processId ?? -1,
                GameStartTime = startTime,
                Playtime = TimeSpan.FromSeconds(game.Playtime),
                CoverImagePath = GetFullCoverImagePath(game),
                Achievements = achievements
            };
        }

        private List<AchievementData> GetGameAchievements(Game game)
        {
            log($"Retrieving achievements for game {game.Name} (ID: {game.Id}, SuccessStory Enabled: {isSuccessStoryAvailable})");
            var achievements = new List<AchievementData>();

            if (!isSuccessStoryAvailable || game == null)
                return achievements;

            try
            {
                var successStory = playniteAPI.Addons.Plugins.FirstOrDefault(p => p.Id == successStoryId);
                if (successStory != null)
                {
                    try
                    {
                        string successStoryDir = Path.Combine(playniteAPI.Paths.ExtensionsDataPath, successStoryId.ToString(), "SuccessStory");
                        if (Directory.Exists(successStoryDir))
                        {
                            string achievementsFile = Path.Combine(successStoryDir, $"{game.Id}.json");
                            if (File.Exists(achievementsFile))
                            {
                                string achievementsJson = File.ReadAllText(achievementsFile);
                                achievements = ParseSuccessStoryData(achievementsJson);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Failed to get achievements from file: {ex.Message}");
                    }
                }
                logger.Info($"Retrieved {achievements.Count} achievements for game {game.Name}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error retrieving achievements from SuccessStory: {ex.Message}");
            }

            return achievements;
        }

        private string GetFullCoverImagePath(Game game)
        {
            if (string.IsNullOrEmpty(game.CoverImage))
                return null;

            try
            {
                return playniteAPI.Database.GetFullFilePath(game.CoverImage);
            }
            catch (Exception ex)
            {
                logger.Error($"Error getting cover image path: {ex}");
                return null;
            }
        }

        private Process FindRunningGameProcess(Game game)
        {
            if (game == null) return null;

            try
            {
                var gameExecutables = new List<string>();
                try
                {
                    gameExecutables = Directory.GetFiles(game.InstallDirectory, "*.exe", SearchOption.AllDirectories)
                        .Select(path => Path.GetFileNameWithoutExtension(path))
                        .ToList();

                    var additionalNames = new List<string>();
                    foreach (var exe in gameExecutables)
                    {
                        additionalNames.Add(exe.Replace("-", ""));
                        additionalNames.Add(exe.Replace("_", ""));
                        additionalNames.Add(exe.Replace(" ", ""));
                        additionalNames.Add(exe.Replace("-", " "));
                        additionalNames.Add(exe.Replace("_", " "));
                        additionalNames.Add(exe.Replace(" ", " "));
                    }
                    gameExecutables.AddRange(additionalNames);

                    log($"Found {gameExecutables.Count} potential game executables in {game.InstallDirectory}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Error scanning game directory: {ex.Message}");
                }

                string gameName = game.Name;
                string[] gameNameWords = gameName.ToLower().Split(new char[] { ' ', '-', '_', ':', '.', '(', ')', '[', ']' },
                    StringSplitOptions.RemoveEmptyEntries);

                log($"Game name for matching: {gameName}, split into {gameNameWords.Length} words");

                Process[] allProcesses = Process.GetProcesses()
                    .Where(p =>
                    {
                        try
                        {
                            return p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToArray();

                var candidates = new List<Process>();
                var nameMatchCandidates = new List<Process>();
                var titleMatchCandidates = new List<(Process Process, int MatchCount)>();
                var inaccessibleCandidates = new List<Process>();

                foreach (var p in allProcesses)
                {
                    try
                    {
                        if (!p.HasExited && p.WorkingSet64 > 100 * 1024 * 1024)
                        {
                            bool nameMatches = gameExecutables.Any(exe =>
                                string.Equals(exe, p.ProcessName, StringComparison.OrdinalIgnoreCase));

                            int titleMatchScore = 0;
                            if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                            {
                                string[] windowTitleWords = p.MainWindowTitle.ToLower().Split(new char[] { ' ', '-', '_', ':', '.', '(', ')', '[', ']' },
                                    StringSplitOptions.RemoveEmptyEntries);
                                titleMatchScore = gameNameWords.Count(gameWord =>
                                    windowTitleWords.Any(titleWord => titleWord.Contains(gameWord) || gameWord.Contains(titleWord)));
                            }

                            try
                            {
                                var modulePath = p.MainModule.FileName;
                                if (modulePath.IndexOf(game.InstallDirectory, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    candidates.Add(p);
                                }
                                else if (nameMatches)
                                {
                                    nameMatchCandidates.Add(p);
                                }
                                else if (titleMatchScore > 0)
                                {
                                    titleMatchCandidates.Add((p, titleMatchScore));
                                }
                            }
                            catch
                            {
                                if (nameMatches)
                                {
                                    nameMatchCandidates.Add(p);
                                }
                                else if (titleMatchScore > 0)
                                {
                                    titleMatchCandidates.Add((p, titleMatchScore));
                                }
                                else
                                {
                                    inaccessibleCandidates.Add(p);
                                }
                            }
                        }
                    }
                    catch { /* Skip processes we can't access */ }
                }

                if (candidates.Count > 0)
                {
                    var bestMatch = candidates.OrderByDescending(p => p.WorkingSet64).First();
                    log($"Found process with matching path: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    return bestMatch;
                }

                if (titleMatchCandidates.Count > 0)
                {
                    var bestMatch = titleMatchCandidates.OrderByDescending(t => t.MatchCount)
                                                        .ThenByDescending(t => t.Process.WorkingSet64)
                                                        .First().Process;
                    log($"Found process with matching window title: {bestMatch.ProcessName} (ID: {bestMatch.Id}, Title: {bestMatch.MainWindowTitle})");
                    return bestMatch;
                }

                if (nameMatchCandidates.Count > 0)
                {
                    var bestMatch = nameMatchCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    log($"Found process with matching name: {bestMatch.ProcessName} (ID: {bestMatch.Id})");
                    return bestMatch;
                }

                if (inaccessibleCandidates.Count > 0)
                {
                    var bestGuess = inaccessibleCandidates.OrderByDescending(p => p.WorkingSet64).First();
                    log($"Using best guess process: {bestGuess.ProcessName} (ID: {bestGuess.Id})");
                    return bestGuess;
                }
            }
            catch (Exception ex)
            {
                log($"Error finding game process: {ex.Message}", "ERROR");
            }

            return null;
        }

        private void log(string msg, string tag = "DEBUG")
        {
            if (true)
            {
                Debug.WriteLine("GameOverlay[" + tag + "]: " + msg);
            }
            logger.Debug(msg);
        }

        private void ShowPlaynite()
        {
            ShowPlaynite(false);
        }

        private void ShowPlaynite(bool forceFullscreen = false)
        {
            try
            {
                if (forceFullscreen)
                {
                    Process.Start(Path.Combine(playniteAPI.Paths.ApplicationPath, "Playnite.FullscreenApp.exe"));
                    return;
                }
                Process[] processes = Process.GetProcessesByName("Playnite.DesktopApp");
                if (processes.Length > 0)
                {
                    Process.Start(Path.Combine(playniteAPI.Paths.ApplicationPath, "Playnite.DesktopApp.exe"));
                    return;
                }
                processes = Process.GetProcessesByName("Playnite.FullscreenApp");
                if (processes.Length > 0)
                {
                    Process.Start(Path.Combine(playniteAPI.Paths.ApplicationPath, "Playnite.FullscreenApp.exe"));
                    return;
                }
                else
                {
                    log("Could not find any Playnite process to activate", "WARNING");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error showing Playnite: {ex}");
            }
        }

        private List<AchievementData> ParseSuccessStoryData(string jsonData)
        {
            var achievements = new List<AchievementData>();
            try
            {
                var successStoryData = JsonSerializer.Deserialize<SuccessStoryData>(jsonData);
                if (successStoryData?.Items == null)
                {
                    logger.Error("Failed to parse SuccessStory data: Items is null");
                    return achievements;
                }

                foreach (var item in successStoryData.Items)
                {
                    bool isUnlocked = !string.IsNullOrEmpty(item.DateUnlockedStr);
                    DateTime? unlockDate = null;

                    if (isUnlocked)
                    {
                        if (DateTime.TryParse(item.DateUnlockedStr, out DateTime parsedDate))
                        {
                            unlockDate = parsedDate;
                        }
                    }

                    achievements.Add(new AchievementData
                    {
                        Name = item.Name,
                        Description = item.Description,
                        IsUnlocked = isUnlocked,
                        UnlockDate = unlockDate,
                        IconUrl = isUnlocked ? item.UrlUnlocked : item.UrlLocked
                    });
                }

                log($"Successfully parsed {achievements.Count} achievements from SuccessStory");
            }
            catch (Exception ex)
            {
                log($"Error parsing SuccessStory file: {ex.Message}", "ERROR");
            }

            return achievements;
        }

        #region SDL2
        private IntPtr controller = IntPtr.Zero;
        private bool sdlInitialized = false;
        private int controllerId = -1;
        private DispatcherTimer controllerTimerFast;
        private DispatcherTimer controllerTimerSlow;

        private void InitializeController()
        {
            try
            {
                log("Initializing SDL controller support", "SDL_GLOBAL");
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) < 0)
                {
                    string error = SDL.SDL_GetError();
                    log($"SDL_GLOBAL could not initialize! SDL Error: {error}", "SDL_GLOBAL_ERROR");
                    return;
                }

                sdlInitialized = true;
                log("SDL_GLOBAL initialized successfully", "SDL_GLOBAL");

                int numJoysticks = SDL.SDL_NumJoysticks();
                log($"Found {numJoysticks} joysticks/controllers", "SDL_GLOBAL");

                for (int i = 0; i < numJoysticks; i++)
                {
                    if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                    {
                        controllerId = i;
                        log($"Found compatible game controller at index {i}", "SDL_GLOBAL");
                        controller = SDL.SDL_GameControllerOpen(controllerId);
                        if (controller == IntPtr.Zero)
                        {
                            log($"Could not open controller! SDL Error: {SDL.SDL_GetError()}", "SDL_GLOBAL_ERROR");
                            return;
                        }
                        string mapping = SDL.SDL_GameControllerMapping(controller);
                        log($"Controller mapping: {mapping}", "SDL_GLOBAL_DEBUG");
                        break;
                    }
                }

                if (controllerId == -1)
                {
                    log("No compatible game controllers found", "SDL_GLOBAL");
                    return;
                }

                log("Setting up controller polling timer", "SDL_GLOBAL");
                controllerTimerFast = new DispatcherTimer();
                controllerTimerFast.Interval = TimeSpan.FromMilliseconds(8);
                controllerTimerFast.Tick += PollControllerInput;

                controllerTimerSlow = new DispatcherTimer();
                controllerTimerSlow.Interval = TimeSpan.FromMilliseconds(32);
                controllerTimerSlow.Tick += PollControllerInput;
                controllerTimerSlow.Start();
                log("Controller polling timer started", "SDL_GLOBAL");
            }
            catch (Exception ex)
            {
                log($"Error initializing SDL: {ex.Message}", "SDL_GLOBAL_ERROR");
                log($"Stack trace: {ex.StackTrace}", "SDL_GLOBAL_ERROR");
            }
        }

        private void CloseController()
        {
            if (controller != IntPtr.Zero)
            {
                SDL.SDL_GameControllerClose(controller);
                controller = IntPtr.Zero;
                log("Controller closed", "SDL_GLOBAL");
            }
            if (controllerTimerFast != null)
            {
                controllerTimerFast.Stop();
            }
            if (controllerTimerSlow != null)
            {
                controllerTimerSlow.Stop();
            }
        }

        private void PollControllerInput(object sender, EventArgs e)
        {
            if (controller == IntPtr.Zero)
            {
                log("Controller not open, skipping polling", "SDL_GLOBAL");
                return;
            }

            SDL.SDL_Event sdlEvent;
            while (SDL.SDL_PollEvent(out sdlEvent) != 0)
            {
                log($"SDL_GLOBAL event type: {sdlEvent.type}", "SDL_GLOBAL_EVENT");
                if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED)
                {
                    log($"Controller device added: {sdlEvent.cdevice.which}", "SDL_GLOBAL_EVENT");
                }
                else if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED)
                {
                    log($"Controller device removed: {sdlEvent.cdevice.which}", "SDL_GLOBAL_EVENT");
                }
            }

            SDL.SDL_GameControllerUpdate();

            bool startPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START) == 1;
            bool backPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK) == 1;
            bool guidePressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE) == 1;

            if ((Settings.ControllerShortcut == ControllerShortcut.StartBack && startPressed && backPressed) ||
                (Settings.ControllerShortcut == ControllerShortcut.Guide && guidePressed))
            {
                var runningGame = playniteAPI.Database.Games.FirstOrDefault(g => g.IsRunning);
                if (runningGame != null)
                {
                    if (overlayWindow.IsVisible)
                        overlayWindow.Hide();
                    else
                        ShowGameOverlay(runningGame);
                }
                else
                {
                    ShowPlaynite(true);
                }
            }
        }
        #endregion

        #region Settings
        public override ISettings GetSettings(bool firstRunSettings)
        {
            if (settings == null)
            {
                settings = new OverlaySettings(this);
            }
            return settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
        {
            return new OverlaySettingsView();
        }

        public OverlaySettings Settings
        {
            get
            {
                if (settings == null)
                {
                    settings = (OverlaySettings)GetSettings(false);
                }
                return settings;
            }
        }
        #endregion

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private void CheckSuccessStoryAvailability()
        {
            try
            {
                var plugins = playniteAPI.Addons.Plugins;
                isSuccessStoryAvailable = plugins.Any(p => p.Id == successStoryId);

                if (isSuccessStoryAvailable)
                {
                    log("SuccessStory plugin detected, achievement integration enabled");
                }
                else
                {
                    log("SuccessStory plugin not found, achievement integration disabled");
                }
            }
            catch (Exception ex)
            {
                log($"Error checking for SuccessStory plugin: {ex.Message}", "ERROR");
                isSuccessStoryAvailable = false;
            }
        }
    }

    public class AchievementData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsUnlocked { get; set; }
        public DateTime? UnlockDate { get; set; }
        public string IconUrl { get; set; }
    }

    public class SuccessStoryData
    {
        [JsonPropertyName("Items")]
        public List<SuccessStoryAchievement> Items { get; set; }

        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }

    public class SuccessStoryAchievement
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Description")]
        public string Description { get; set; }

        [JsonPropertyName("DateUnlocked")]
        public string DateUnlockedStr { get; set; }

        [JsonPropertyName("UrlUnlocked")]
        public string UrlUnlocked { get; set; }

        [JsonPropertyName("UrlLocked")]
        public string UrlLocked { get; set; }
    }

    public class OverlaySettings : ObservableObject, ISettings
    {
        private readonly PlayniteControlCenter plugin;

        private ControllerShortcut _controllerShortcut = ControllerShortcut.StartBack;
        private bool _debugMode = false;
        private AspectRatio _aspectRatio = AspectRatio.Portrait;

        public ControllerShortcut ControllerShortcut
        {
            get => _controllerShortcut;
            set => SetValue(ref _controllerShortcut, value);
        }

        public bool DebugMode
        {
            get => _debugMode;
            set => SetValue(ref _debugMode, value);
        }

        public AspectRatio AspectRatio
        {
            get => _aspectRatio;
            set => SetValue(ref _aspectRatio, value);
        }

        private ControllerShortcut _controllerShortcutBackup;
        private bool _debugModeBackup;

        public OverlaySettings()
        {
        }

        public OverlaySettings(PlayniteControlCenter plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<OverlaySettings>();
            if (savedSettings != null)
            {
                ControllerShortcut = savedSettings.ControllerShortcut;
                DebugMode = savedSettings.DebugMode;
                AspectRatio = savedSettings.AspectRatio;
            }
        }

        public void BeginEdit()
        {
            _controllerShortcutBackup = ControllerShortcut;
            _debugModeBackup = DebugMode;
        }

        public void CancelEdit()
        {
            ControllerShortcut = _controllerShortcutBackup;
            DebugMode = _debugModeBackup;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(this);
            plugin.ReloadOverlay(this);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }

    public enum ControllerShortcut
    {
        [Description("View + Menu (Back + Start)")]
        StartBack,

        [Description("Xbox Button (Guide Button)")]
        Guide
    }

    public enum AspectRatio
    {
        Portrait,
        Landscape,
        Square
    }
}
using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using SDL2;
using System.Windows.Input;
using System.Windows.Interop;
using System.Media;
using System.IO;
using Playnite.SDK;
using PlayniteControlCenter; // Para GameOverlayData

namespace PlayniteControlCenter
{
    public partial class OverlayWindow : Window
    {
        private OverlaySettings Settings;
        private IPlayniteAPI PlayniteApi;
        private volatile GameOverlayData _currentGameData;
        private readonly DispatcherTimer clockTimer;
        private DispatcherTimer controllerTimer;
        private IntPtr controller = IntPtr.Zero;
        private bool sdlInitialized = false;
        private int controllerId = -1;

        private SoundPlayer _navigateSound;
        private SoundPlayer _selectSound;
        private bool _isGameGridVisible = false;

        public event Action OnShowPlayniteRequested; // Declarado aqui

        public OverlayWindow(OverlaySettings settings, IPlayniteAPI playniteApi)
        {
            InitializeComponent();
            Settings = settings;
            PlayniteApi = playniteApi;
            this.WindowState = WindowState.Maximized;

            // Inicializar sons
            try
            {
                // Usar o diretório do assembly da extensão em vez de BaseDirectory
                string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string navigatePath = Path.Combine(baseDir, "audio", "menu_navigate.wav");
                string selectPath = Path.Combine(baseDir, "audio", "menu_select.wav");

                log($"Caminho dos sons: {navigatePath}, {selectPath}", "SOUND_DEBUG");
                if (!File.Exists(navigatePath) || !File.Exists(selectPath))
                {
                    log($"Arquivos de som não encontrados: {navigatePath}, {selectPath}", "SOUND_ERROR");
                    System.Windows.MessageBox.Show($"Arquivos de som não encontrados:\n{navigatePath}\n{selectPath}");
                }
                else
                {
                    _navigateSound = new SoundPlayer(navigatePath);
                    _selectSound = new SoundPlayer(selectPath);
                    _navigateSound.Load();
                    _selectSound.Load();
                    log("Sons carregados com sucesso.", "SOUND");
                }
            }
            catch (Exception ex)
            {
                log($"Erro ao carregar sons: {ex.Message}", "SOUND_ERROR");
                System.Windows.MessageBox.Show($"Erro ao carregar sons: {ex.Message}");
            }

            // Clock timer
            clockTimer = new DispatcherTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += UpdateClock;
            clockTimer.Start();

            // Controller setup
            InitializeController();

            // Foco inicial
            ButtonInicio.Focus();
        }

        private void log(string msg, string tag = "DEBUG")
        {
            if (Settings != null && Settings.DebugMode)
            {
                Debug.WriteLine("GameOverlay[" + tag + "]: " + msg);
            }
        }

        public void ShowOverlay()
        {
            ResumeTimers();
            UpdateClock(null, EventArgs.Empty);
            this.Show();
            this.Activate();
            ForceFocusOverlay();
            ButtonInicio.Focus();
        }

        public new void Hide()
        {
            PauseTimers();
            base.Hide();
        }

        private void ForceFocusOverlay()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            IntPtr foregroundWindow = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
            uint currentThread = GetCurrentThreadId();
            AttachThreadInput(currentThread, foregroundThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThread, foregroundThread, false);
        }

        private void ResumeTimers()
        {
            Dispatcher.Invoke(() =>
            {
                clockTimer.Start();
                if (controllerTimer != null) controllerTimer.Start();
            });
        }

        private void PauseTimers()
        {
            Dispatcher.Invoke(() =>
            {
                clockTimer.Stop();
                if (controllerTimer != null) controllerTimer.Stop();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            CloseController();
            if (sdlInitialized)
            {
                SDL.SDL_Quit();
                sdlInitialized = false;
            }
            PauseTimers();
            _navigateSound?.Dispose();
            _selectSound?.Dispose();
            base.OnClosed(e);
        }

        public void UpdateGameOverlay(GameOverlayData gameData)
        {
            Dispatcher.Invoke(() =>
            {
                _currentGameData = gameData;
                DataContext = this;
            });
        }

        private void UpdateClock(object sender, EventArgs e)
        {
            // Mantido para compatibilidade, mas não exibido na UI por agora
        }

        // Eventos dos botões principais
        private void ButtonInicio_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectSound();
            OnShowPlayniteRequested?.Invoke(); // Disparar evento para voltar ao Playnite
            Hide();
        }

        private void ButtonGames_Click(object sender, RoutedEventArgs e)
        {
            _isGameGridVisible = !_isGameGridVisible;
            GameGrid.Visibility = _isGameGridVisible ? Visibility.Visible : Visibility.Collapsed;
            if (_isGameGridVisible)
            {
                ForceFocusOverlay();
                ReturnToGameButton.Focus();
            }
            PlaySelectSound();
        }

        private void ButtonVolume_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectSound();
        }

        private void ButtonUser_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectSound();
        }

        private void ButtonPower_Click(object sender, RoutedEventArgs e)
        {
            PlaySelectSound();
        }

        // Eventos do GameGrid
        private void ReturnToGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGameData != null)
            {
                var proc = FindProcessById(_currentGameData.ProcessId);
                if (proc != null)
                {
                    log($"Retornando ao jogo com ProcessId: {_currentGameData.ProcessId}", "GAME_RETURN");
                    if (IsIconic(proc.MainWindowHandle))
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    }
                    SetForegroundWindow(proc.MainWindowHandle);
                }
                else
                {
                    log("Nenhum processo encontrado para retornar ao jogo.", "GAME_RETURN_ERROR");
                    System.Windows.MessageBox.Show("Nenhum processo encontrado para retornar ao jogo.");
                }
            }
            else
            {
                log("Nenhum jogo ativo detectado (_currentGameData é null).", "GAME_RETURN_ERROR");
                System.Windows.MessageBox.Show("Nenhum jogo ativo detectado.");
            }
            PlaySelectSound();
            _isGameGridVisible = false;
            GameGrid.Visibility = Visibility.Collapsed;
            this.Hide();
        }

        private void CloseGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGameData != null)
            {
                log($"Tentando fechar jogo com GameId: {_currentGameData.GameId}, ProcessId: {_currentGameData.ProcessId}", "GAME_CLOSE");
                try
                {
                    var proc = FindProcessById(_currentGameData.ProcessId);
                    if (proc != null)
                    {
                        var game = PlayniteApi.Database.Games.Get(_currentGameData.GameId);
                        if (game != null && game.IsRunning)
                        {
                            proc.CloseMainWindow();
                            if (!proc.WaitForExit(2000))
                            {
                                proc.Kill();
                                log("Jogo foi forçado a fechar após falha em CloseMainWindow.", "GAME_CLOSE");
                            }
                            else
                            {
                                log("Jogo fechado normalmente via CloseMainWindow.", "GAME_CLOSE");
                            }
                        }
                        else
                        {
                            log("Jogo não está mais marcado como em execução no Playnite.", "GAME_CLOSE");
                        }
                        proc.Close();
                    }
                    else
                    {
                        log("Nenhum processo encontrado para o jogo ativo.", "GAME_CLOSE_ERROR");
                        System.Windows.MessageBox.Show("Nenhum processo encontrado para o jogo ativo.");
                    }
                }
                catch (Exception ex)
                {
                    log($"Erro ao fechar o jogo: {ex.Message}", "GAME_CLOSE_ERROR");
                    System.Windows.MessageBox.Show($"Erro ao fechar o jogo: {ex.Message}");
                }
            }
            else
            {
                log("Nenhum jogo ativo detectado (_currentGameData é null).", "GAME_CLOSE_ERROR");
                System.Windows.MessageBox.Show("Nenhum jogo ativo detectado.");
            }
            PlaySelectSound();
            _isGameGridVisible = false;
            GameGrid.Visibility = Visibility.Collapsed;
            this.Hide();
        }

        private Process FindProcessById(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch
            {
                return null;
            }
        }

        // Navegação por teclado
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_isGameGridVisible)
            {
                if (e.Key == Key.Up || e.Key == Key.Down)
                {
                    if (ReturnToGameButton.IsFocused)
                    {
                        if (e.Key == Key.Down) CloseGameButton.Focus();
                    }
                    else if (CloseGameButton.IsFocused)
                    {
                        if (e.Key == Key.Up) ReturnToGameButton.Focus();
                    }
                    e.Handled = true;
                    PlayNavigateSound();
                }
                else if (e.Key == Key.Enter)
                {
                    if (ReturnToGameButton.IsFocused) ReturnToGameButton_Click(null, null);
                    else if (CloseGameButton.IsFocused) CloseGameButton_Click(null, null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    _isGameGridVisible = false;
                    GameGrid.Visibility = Visibility.Collapsed;
                    ButtonGames.Focus();
                    PlaySelectSound();
                    e.Handled = true;
                }
            }
            else
            {
                if (e.Key == Key.Left || e.Key == Key.Right)
                {
                    if (ButtonInicio.IsFocused)
                    {
                        if (e.Key == Key.Right) ButtonGames.Focus();
                        else if (e.Key == Key.Left) ButtonPower.Focus();
                    }
                    else if (ButtonGames.IsFocused)
                    {
                        if (e.Key == Key.Right) ButtonVolume.Focus();
                        else if (e.Key == Key.Left) ButtonInicio.Focus();
                    }
                    else if (ButtonVolume.IsFocused)
                    {
                        if (e.Key == Key.Right) ButtonUser.Focus();
                        else if (e.Key == Key.Left) ButtonGames.Focus();
                    }
                    else if (ButtonUser.IsFocused)
                    {
                        if (e.Key == Key.Right) ButtonPower.Focus();
                        else if (e.Key == Key.Left) ButtonVolume.Focus();
                    }
                    else if (ButtonPower.IsFocused)
                    {
                        if (e.Key == Key.Right) ButtonInicio.Focus();
                        else if (e.Key == Key.Left) ButtonUser.Focus();
                    }
                    PlayNavigateSound();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    this.Hide();
                    PlaySelectSound();
                    e.Handled = true;
                }
            }
        }

        // Suporte a controle (SDL)
        private void InitializeController()
        {
            try
            {
                log("Initializing SDL controller support", "SDL");
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) < 0)
                {
                    log($"SDL could not initialize! SDL Error: {SDL.SDL_GetError()}", "SDL_ERROR");
                    return;
                }
                sdlInitialized = true;
                int numJoysticks = SDL.SDL_NumJoysticks();
                for (int i = 0; i < numJoysticks; i++)
                {
                    if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                    {
                        controllerId = i;
                        controller = SDL.SDL_GameControllerOpen(controllerId);
                        if (controller == IntPtr.Zero)
                        {
                            log($"Could not open controller! SDL Error: {SDL.SDL_GetError()}", "SDL_ERROR");
                            return;
                        }
                        break;
                    }
                }
                if (controllerId == -1)
                {
                    log("No compatible game controllers found", "SDL");
                    return;
                }
                controllerTimer = new DispatcherTimer();
                controllerTimer.Interval = TimeSpan.FromMilliseconds(16);
                controllerTimer.Tick += PollControllerInput;
                controllerTimer.Start();
            }
            catch (Exception ex)
            {
                log($"Error initializing SDL: {ex.Message}", "SDL_ERROR");
            }
        }

        private void CloseController()
        {
            if (controller != IntPtr.Zero)
            {
                SDL.SDL_GameControllerClose(controller);
                controller = IntPtr.Zero;
                log("Controller closed", "SDL");
            }
        }

        private void PollControllerInput(object sender, EventArgs e)
        {
            if (controller == IntPtr.Zero) return;

            SDL.SDL_GameControllerUpdate();
            bool dpadUp = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP) == 1;
            bool dpadDown = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN) == 1;
            bool dpadLeft = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT) == 1;
            bool dpadRight = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT) == 1;
            bool aPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A) == 1;
            bool bPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B) == 1;

            Dispatcher.Invoke(() =>
            {
                if (_isGameGridVisible)
                {
                    if (dpadUp && !WasButtonPressedLastFrame(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP))
                    {
                        if (CloseGameButton.IsFocused) ReturnToGameButton.Focus();
                        PlayNavigateSound();
                    }
                    if (dpadDown && !WasButtonPressedLastFrame(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN))
                    {
                        if (ReturnToGameButton.IsFocused) CloseGameButton.Focus();
                        PlayNavigateSound();
                    }
                    if (aPressed)
                    {
                        if (ReturnToGameButton.IsFocused) ReturnToGameButton_Click(null, null);
                        else if (CloseGameButton.IsFocused) CloseGameButton_Click(null, null);
                    }
                    if (bPressed)
                    {
                        _isGameGridVisible = false;
                        GameGrid.Visibility = Visibility.Collapsed;
                        ButtonGames.Focus();
                        PlaySelectSound();
                    }
                }
                else
                {
                    if (dpadLeft && !WasButtonPressedLastFrame(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT))
                    {
                        if (ButtonInicio.IsFocused) ButtonPower.Focus();
                        else if (ButtonGames.IsFocused) ButtonInicio.Focus();
                        else if (ButtonVolume.IsFocused) ButtonGames.Focus();
                        else if (ButtonUser.IsFocused) ButtonVolume.Focus();
                        else if (ButtonPower.IsFocused) ButtonUser.Focus();
                        PlayNavigateSound();
                    }
                    if (dpadRight && !WasButtonPressedLastFrame(SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT))
                    {
                        if (ButtonInicio.IsFocused) ButtonGames.Focus();
                        else if (ButtonGames.IsFocused) ButtonVolume.Focus();
                        else if (ButtonVolume.IsFocused) ButtonUser.Focus();
                        else if (ButtonUser.IsFocused) ButtonPower.Focus();
                        else if (ButtonPower.IsFocused) ButtonInicio.Focus();
                        PlayNavigateSound();
                    }
                    if (aPressed)
                    {
                        if (ButtonInicio.IsFocused) ButtonInicio_Click(null, null);
                        else if (ButtonGames.IsFocused) ButtonGames_Click(null, null);
                        else if (ButtonVolume.IsFocused) ButtonVolume_Click(null, null);
                        else if (ButtonUser.IsFocused) ButtonUser_Click(null, null);
                        else if (ButtonPower.IsFocused) ButtonPower_Click(null, null);
                    }
                    if (bPressed)
                    {
                        this.Hide();
                        PlaySelectSound();
                    }
                }
            });
        }

        private bool[] _lastControllerState = new bool[Enum.GetValues(typeof(SDL.SDL_GameControllerButton)).Length];
        private bool WasButtonPressedLastFrame(SDL.SDL_GameControllerButton button)
        {
            int index = (int)button;
            bool wasPressed = _lastControllerState[index];
            _lastControllerState[index] = SDL.SDL_GameControllerGetButton(controller, button) == 1;
            return wasPressed;
        }

        // Métodos de som
        private void PlayNavigateSound()
        {
            try
            {
                if (_navigateSound != null)
                {
                    _navigateSound.Play();
                    log("Som de navegação tocado.", "SOUND");
                }
            }
            catch (Exception ex)
            {
                log($"Erro ao tocar som de navegação: {ex.Message}", "SOUND_ERROR");
                System.Windows.MessageBox.Show($"Erro ao tocar som de navegação: {ex.Message}");
            }
        }

        private void PlaySelectSound()
        {
            try
            {
                if (_selectSound != null)
                {
                    _selectSound.Play();
                    log("Som de seleção tocado.", "SOUND");
                }
            }
            catch (Exception ex)
            {
                log($"Erro ao tocar som de seleção: {ex.Message}", "SOUND_ERROR");
                System.Windows.MessageBox.Show($"Erro ao tocar som de seleção: {ex.Message}");
            }
        }

        #region Win32 API
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private const int SW_RESTORE = 9;
        #endregion
    }
}
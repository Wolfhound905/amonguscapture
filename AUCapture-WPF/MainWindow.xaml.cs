﻿using AmongUsCapture;
using AmongUsCapture.TextColorLibrary;
using AUCapture_WPF.IPC;
using Config.Net;
using ControlzEx.Theming;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Discord;
using Humanizer;
using Color = System.Drawing.Color;

namespace AUCapture_WPF
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public Color NormalTextColor = Color.White;

        private readonly IAppSettings config;

        public UserDataContext context;
        private readonly bool connected;
        private readonly object locker = new object();
        private readonly Queue<string> DeadMessages = new Queue<string>();
        private Task ThemeGeneration;
        private bool Updated = false;

        public MainWindow()
        {
            InitializeComponent();
            string appFolder = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            string appName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName);
            string appExtension = Path.GetExtension(Process.GetCurrentProcess().MainModule.FileName);
            string archivePath = Path.Combine(appFolder, appName + "_Old" + appExtension);
            if (File.Exists(archivePath))
            {
                Updated = true;
                try
                { //Will wait for the other program to exit.
                    var me = Process.GetCurrentProcess();
                    Process[] aProcs = Process.GetProcessesByName(me.ProcessName);
                    aProcs = aProcs.Where(x => x.Id != me.Id).ToArray();
                    if (aProcs != null && aProcs.Length > 0)
                    {
                        aProcs[0].WaitForExit(1000);
                    }
                    File.Delete(archivePath);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Could not delete old file.");
                    
                }
            }
            else
            {
                Updated = false;
            }
                
        
                
            Paragraph p = ConsoleTextBox.Document.Blocks.FirstBlock as Paragraph;
            ConsoleTextBox.Document.Blocks.Clear();
            try
            {
                config = new ConfigurationBuilder<IAppSettings>()
                    .UseJsonFile(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "\\AmongUsCapture\\AmongUsGUI", "Settings.json")).Build();
            }
            catch (Newtonsoft.Json.JsonReaderException e) //Delete file and recreate config
            {
                Console.WriteLine($"Bad config. Clearing.");
                File.Delete(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "\\AmongUsCapture\\AmongUsGUI", "Settings.json"));
                config = new ConfigurationBuilder<IAppSettings>()
                    .UseJsonFile(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "\\AmongUsCapture\\AmongUsGUI", "Settings.json")).Build();
            }
            
            context = new UserDataContext(DialogCoordinator.Instance, config);
            DataContext = context;
            config.PropertyChanged += ConfigOnPropertyChanged;
            App.handler.OnReady += (sender, args) =>
            {
                App.socket.AddHandler(App.handler);
            };

            GameMemReader.getInstance().GameStateChanged += GameStateChangedHandler;
            GameMemReader.getInstance().PlayerChanged += UserForm_PlayerChanged;
            GameMemReader.getInstance().ChatMessageAdded += OnChatMessageAdded;
            GameMemReader.getInstance().JoinedLobby += OnJoinedLobby;
            GameMemReader.getInstance().GameOver += OnGameOver;
            IPCAdapter.getInstance().OnToken += (sender, token) =>
            {
                this.BeginInvoke((w) =>
                {
                    if (!w.context.Settings.FocusOnToken)
                    {
                        return;
                    }

                    if (!w.IsVisible)
                    {
                        w.Show();
                    }

                    if (w.WindowState == WindowState.Minimized)
                    {
                        w.WindowState = WindowState.Normal;
                    }

                    w.Activate();
                    w.Focus();         // important
                });
            };
            if (!context.Settings.discordTokenEncrypted) //Encrypt discord token if it is not encrypted.
            {
                context.Settings.discordToken = JsonConvert.SerializeObject(encryptToken(context.Settings.discordToken));
                context.Settings.discordTokenEncrypted = true;
            }
            byte[] encryptedBuff = JsonConvert.DeserializeObject<byte[]>(context.Settings.discordToken);
            discordTokenBox.Password = decryptToken(encryptedBuff);


            System.Windows.Media.Color savedColor;
            try
            {
                savedColor = JsonConvert.DeserializeObject<System.Windows.Media.Color>(context.Settings.SelectedAccent);
                AccentColorPicker.SelectedColor = savedColor;
                string BaseColor;
                if (context.Settings.DarkMode)
                {
                    BaseColor = ThemeManager.BaseColorDark;
                }
                else
                {
                    BaseColor = ThemeManager.BaseColorLight;
                }
                Theme newTheme = new Theme(name: "CustomTheme",
                    displayName: "CustomTheme",
                    baseColorScheme: BaseColor,
                    colorScheme: "CustomAccent",
                    primaryAccentColor: savedColor,
                    showcaseBrush: new SolidColorBrush(savedColor),
                    isRuntimeGenerated: true,
                    isHighContrast: false);

                ThemeManager.Current.ChangeTheme(this, newTheme);
            }
            catch (Exception e)
            { }

            

            //ApplyDarkMode();
        }

        public async void Update()
        {
            Version version = new Version(context.Version);
            Version latestVersion = new Version(context.LatestVersion);
#if PUBLISH
            if (latestVersion.CompareTo(version) > 0)
            {
                var selection = await context.DialogCoordinator.ShowMessageAsync(context, "Caution",
                    $"We've detected you're using an older version of AmongUsCapture!\nYour version: {version}\nLatest version: {latestVersion}",
                    MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings
                    {
                        AffirmativeButtonText =
                            "Update",
                        NegativeButtonText = "No thanks", DefaultButtonFocus = MessageDialogResult.Affirmative
                    });
                if (selection == MessageDialogResult.Negative)
                {
                    selection = await context.DialogCoordinator.ShowMessageAsync(context, "Warning",
                        $"Having an older version could cause compatibility issues with AutoMuteUs.\nWe can automagically update you to {latestVersion}.",
                        MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings
                        {
                            AffirmativeButtonText =
                                "Update",
                            NegativeButtonText = "no ty", DefaultButtonFocus = MessageDialogResult.Affirmative
                        });
                }
                if (selection == MessageDialogResult.Affirmative)
                {
                    var DownloadProgress =
                        await context.DialogCoordinator.ShowProgressAsync(context, "Step 1/3 - Downloading", "Percent: 0% (0/0)", isCancelable:false);
                    DownloadProgress.Maximum = 100;
                    using (var client = new WebClient())
                    {
                        var downloadPath = Path.GetTempFileName();
                        client.DownloadProgressChanged += (sender, args) =>
                        {
                            DownloadProgress.SetProgress(args.ProgressPercentage);
                            DownloadProgress.SetMessage($"Percent: {args.ProgressPercentage}% ({args.BytesReceived.Bytes().Humanize("#.##")}/{args.TotalBytesToReceive.Bytes().Humanize("#.##")})");
                        };
                        client.DownloadFileCompleted += async (sender, args) =>
                        {
                            if (args.Error is not null)
                            {
                                await DownloadProgress.CloseAsync();
                                var errorBox = await context.DialogCoordinator.ShowMessageAsync(context, "ERROR",
                                    args.Error.Message, MessageDialogStyle.AffirmativeAndNegative,
                                    new MetroDialogSettings
                                    {
                                        AffirmativeButtonText = "retry",
                                        NegativeButtonText = "cancel",
                                        DefaultButtonFocus = MessageDialogResult.Affirmative
                                    });
                                if (errorBox == MessageDialogResult.Affirmative)
                                {
                                    await Task.Factory.StartNew(Update, TaskCreationOptions.LongRunning);
                                }
                            }
                            else
                            {
                                DownloadProgress.SetTitle("Step 2/3 - Extracting");
                                DownloadProgress.SetMessage("Please wait, we may go unresponsive but don't close the window, we will restart the program after.");
                                DownloadProgress.SetIndeterminate();
                                if (!Directory.Exists(Path.Join(
                                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "\\AmongUsCapture\\AmongUsGUI\\Update")))
                                {
                                    Directory.CreateDirectory(Path.Join(
                                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                        "\\AmongUsCapture\\AmongUsGUI\\Update"));
                                }

                                using (ZipArchive archive = ZipFile.OpenRead(downloadPath))
                                {
                                    try
                                    {
                                        var entry = archive.Entries.First(x => x.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                                        entry.ExtractToFile(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                            "\\AmongUsCapture\\AmongUsGUI\\Update", "AmongUsCapture.exe"), true);

                                    }
                                    catch (Exception e)
                                    {
                                        var errorBox = await context.DialogCoordinator.ShowMessageAsync(context, "ERROR",
                                            e.Message, MessageDialogStyle.AffirmativeAndNegative,
                                            new MetroDialogSettings
                                            {
                                                AffirmativeButtonText = "retry",
                                                NegativeButtonText = "cancel",
                                                DefaultButtonFocus = MessageDialogResult.Affirmative
                                            });
                                        if (errorBox == MessageDialogResult.Affirmative)
                                        {
                                            await Task.Factory.StartNew(Update, TaskCreationOptions.LongRunning);
                                        }
                                    }
                                }


                                //You can't delete a running application. But you can rename it.
                                string appFolder = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                                string appName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName);
                                string appExtension = Path.GetExtension(Process.GetCurrentProcess().MainModule.FileName);
                                string archivePath = Path.Combine(appFolder, appName + "_Old" + appExtension);

                                DownloadProgress.SetTitle("Step 3/3 - Copying files");
                                DownloadProgress.SetMessage("Finishing up..");
                                File.Move(Process.GetCurrentProcess().MainModule.FileName, archivePath);

                                File.Move(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "\\AmongUsCapture\\AmongUsGUI\\Update", "AmongUsCapture.exe"),
                                    Path.Combine(appFolder, appName + appExtension), true);
                                Application.Current.Invoke(()=>
                                {
                                    IPCAdapter.getInstance().mutex.ReleaseMutex(); //Release the mutex so the other app does not see us. 
                                    Process.Start(Path.Combine(appFolder, appName + appExtension));
                                    Environment.Exit(0);
                                });

                            }
                        };
                        var downloaderClient = client.DownloadFileTaskAsync(context.LatestReleaseAssetURL, downloadPath);
                    }
                }
            }
#endif
        }
        private string decryptToken(byte[] EncryptedBytes)
        {
            byte[] protectedBytes = ProtectedData.Unprotect(EncryptedBytes, null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(protectedBytes, 0, protectedBytes.Length);
        }

        private byte[] encryptToken(string token)
        {
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(token);
            byte[] protectedBytes = ProtectedData.Protect(buffer, null, DataProtectionScope.CurrentUser);
            return protectedBytes;
        }
        private void OnGameOver(object? sender, GameOverEventArgs e)
        {
            Console.WriteLine(JsonConvert.SerializeObject(e, Formatting.Indented, new StringEnumConverter()));
        }


        private void UserForm_PlayerChanged(object sender, PlayerChangedEventArgs e)
        {
            if (e.Action == PlayerAction.Died)
            {
                DeadMessages.Enqueue($"{PlayerColorToColorOBJ(e.Color).ToTextColor()}{e.Name}{NormalTextColor.ToTextColor()}: {e.Action}");
            }
            else
            {
                AmongUsCapture.Settings.conInterface.WriteModuleTextColored("PlayerChange", Color.DarkKhaki, $"{PlayerColorToColorOBJ(e.Color).ToTextColor()}{e.Name}{NormalTextColor.ToTextColor()}: {e.Action}");
            }

            //Program.conInterface.WriteModuleTextColored("GameMemReader", Color.Green, e.Name + ": " + e.Action);
        }

        private void OnChatMessageAdded(object sender, ChatMessageEventArgs e)
        {
            AmongUsCapture.Settings.conInterface.WriteModuleTextColored("CHAT", Color.DarkKhaki,
                $"{PlayerColorToColorOBJ(e.Color).ToTextColor()}{e.Sender}{NormalTextColor.ToTextColor()}: {e.Message}");
            //WriteLineToConsole($"[CHAT] {e.Sender}: {e.Message}");
        }
        

        private void OnJoinedLobby(object sender, LobbyEventArgs e)
        {
            GameCodeBox.BeginInvoke(a => a.Text = e.LobbyCode);
            this.BeginInvoke(a =>
            {
                if (context.Settings.AlwaysCopyGameCode)
                {
                    Clipboard.SetText(e.LobbyCode);
                }
            });
            MapBox.BeginInvoke(a => a.Text = e.Map.ToString());
        }

        private Color PlayerColorToColorOBJ(PlayerColor pColor)
        {
            Color OutputCode = Color.White;
            switch (pColor)
            {
                case PlayerColor.Red:
                    OutputCode = Color.Red;
                    break;
                case PlayerColor.Blue:
                    OutputCode = Color.RoyalBlue;
                    break;
                case PlayerColor.Green:
                    OutputCode = Color.Green;
                    break;
                case PlayerColor.Pink:
                    OutputCode = Color.Magenta;
                    break;
                case PlayerColor.Orange:
                    OutputCode = Color.Orange;
                    break;
                case PlayerColor.Yellow:
                    OutputCode = Color.Yellow;
                    break;
                case PlayerColor.Black:
                    OutputCode = Color.Gray;
                    break;
                case PlayerColor.White:
                    OutputCode = Color.White;
                    break;
                case PlayerColor.Purple:
                    OutputCode = Color.MediumPurple;
                    break;
                case PlayerColor.Brown:
                    OutputCode = Color.SaddleBrown;
                    break;
                case PlayerColor.Cyan:
                    OutputCode = Color.Cyan;
                    break;
                case PlayerColor.Lime:
                    OutputCode = Color.Lime;
                    break;
            }

            return OutputCode;
        }

        private void ConfigOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "fontSize")
            {
                ConsoleTextBox.BeginInvoke(tb =>
                {
                    tb.Document.FontSize = config.fontSize;
                    //foreach (var block in tb.Document.Blocks)
                    //{
                    //    block.FontSize = config.fontSize;
                    //}
                }, DispatcherPriority.Input);
            }
        }

        private void SetDefaultThemeColor()
        {
            if (config.ranBefore)
            {
                return;
            }

            config.ranBefore = true;
            ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.SyncAll;
            ThemeManager.Current.SyncTheme();
            Theme newTheme = ThemeManager.Current.DetectTheme();
            config.DarkMode = newTheme.BaseColorScheme == ThemeManager.BaseColorDark;
            Darkmode_toggleswitch.IsOn = config.DarkMode;
        }

        public static void OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                // throw 
            }
        }
        private void ApplyDarkMode()
        {
            if (config.DarkMode)
            {
                ThemeManager.Current.ChangeThemeBaseColor(this, ThemeManager.BaseColorDark);
                NormalTextColor = Color.White;

            }
            else
            {
                NormalTextColor = Color.Black;
                ThemeManager.Current.ChangeThemeBaseColor(this, ThemeManager.BaseColorLight);
            }
        }

        private void Settings(object sender, RoutedEventArgs e)
        {
            // Open up the settings flyout
            //Cracked();
            SettingsFlyout.IsOpen = true;
        }

        private void Darkmode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!(sender is ToggleSwitch toggleSwitch))
            {
                return;
            }

            ApplyDarkMode();
        }

        private void ManualConnect_Click(object sender, RoutedEventArgs e)
        {
            //Open up the manual connection flyout.
            ManualConnectionFlyout.IsOpen = true;

        }

        private void GameStateChangedHandler(object sender, GameStateChangedEventArgs e)
        {
            setCurrentState(e.NewState.ToString());
            while (DeadMessages.Count > 0)
            {
                AmongUsCapture.Settings.conInterface.WriteModuleTextColored("PlayerChange", Color.DarkKhaki, DeadMessages.Dequeue());
            }

            AmongUsCapture.Settings.conInterface.WriteModuleTextColored("GameMemReader", Color.Lime,
                $"State changed to {Color.Cyan.ToTextColor()}{e.NewState}");
            if (e.NewState == GameState.MENU)
            {
                setGameCode("");
                MapBox.BeginInvoke(a => a.Text = "");
            }
            //Program.conInterface.WriteModuleTextColored("GameMemReader", Color.Green, "State changed to " + e.NewState);
        }

        public void setGameCode(string gamecode)
        {
            GameCodeBox.BeginInvoke(tb => { GameCodeBox.Text = gamecode; });
        }

        public void setCurrentState(string state)
        {
            StatusBox.BeginInvoke(tb => { tb.Text = state; });
        }

        public void setConnectionStatus(bool connected)
        {
            if (connected)
            {
                ThemeManager.Current.ChangeThemeColorScheme(this, "Green");
            }
            else
            {
                ThemeManager.Current.ChangeThemeColorScheme(this, "Red");
            }
        }

        public void WriteConsoleLineFormatted(string moduleName, Color moduleColor, string message)
        {
            //Outputs a message like this: [{ModuleName}]: {Message}
            WriteColoredText(
                $"{NormalTextColor.ToTextColor()}[{moduleColor.ToTextColor()}{moduleName}{NormalTextColor.ToTextColor()}]: {message}");
        }

        public void WriteColoredText(string ColoredText)
        {
            ConsoleTextBox.BeginInvoke(tb =>
            {
                Paragraph paragraph = new Paragraph();
                foreach (ColoredString part in TextColor.toParts(ColoredText))
                {
                    //Foreground="{DynamicResource MahApps.Brushes.Text}"
                    Run run = new Run(part.text);

                    if (part.textColor.ToTextColor() != NormalTextColor.ToTextColor())
                    {
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(part.textColor.A,
                            part.textColor.R, part.textColor.G, part.textColor.B));
                    }
                    paragraph.Inlines.Add(run);
                    paragraph.LineHeight = 1;
                    //this.AppendText(part.text, part.textColor, false);
                }
                tb.Document.Blocks.Add(paragraph);
                tb.ScrollToEnd();
            }, DispatcherPriority.Input);
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Factory.StartNew(Update, TaskCreationOptions.LongRunning);
            if (Updated)
            {
                this.ShowMessageAsync("Update successful!", "The update was successful. Happy auto-muting",
                    MessageDialogStyle.Affirmative);
            }

        }

        private void TestFillConsole(int entries) //Helper test method to see if filling console works.
        {
            for (int i = 0; i < entries; i++)
            {
                string nonString = "Wow! Look at this pretty text!";
                WriteConsoleLineFormatted("Rainbow", TextColor.Rainbow((float)i / entries),
                    TextColor.getRainbowText(nonString, i));
            }

            ;
            //this.WriteColoredText(getRainbowText("This is a Pre-Release from Carbon's branch."));
        }

        public bool Cracked()
        {
            PlayGotEm();
            MessageDialogResult x = MessageDialogResult.Affirmative;
            x = this.ShowMessageAsync("Uh oh.",
                "We have detected that you are running an unsupported version of the game. This may or may not work.",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "I understand",
                    NegativeButtonText = "Exit",
                    ColorScheme = MetroDialogColorScheme.Theme,
                    DefaultButtonFocus = MessageDialogResult.Negative
                }).Result;
            return x == MessageDialogResult.Affirmative;
        }
        public void PlayGotEm()
        {
            this.BeginInvoke((win) =>
            {
                win.MemeFlyout.IsOpen = true;
                win.MemePlayer.Position = TimeSpan.Zero;
            });


        }

        private void MainWindow_OnContentRendered(object? sender, EventArgs e)
        {
            //TestFillConsole(10);
            //setCurrentState("GAMESTATE");
            //setGameCode("GAMECODE");
            SetDefaultThemeColor();

            ApplyDarkMode();
            byte[] encryptedBuff = JsonConvert.DeserializeObject<byte[]>(context.Settings.discordToken);
            if (decryptToken(encryptedBuff) != "")
            {
                App.handler.Init(decryptToken(encryptedBuff));
            }
            else
            {
                AmongUsCapture.Settings.conInterface.WriteModuleTextColored("Discord", Color.Red, "You do not have a self-host discord token set. Enabling this in settings will increase performance.");
            }
        }

        private void SubmitConnectButton_OnClick(object sender, RoutedEventArgs e)
        {
            IPCAdapter.getInstance().SendToken(config.host, config.connectCode);
            ManualConnectionFlyout.IsOpen = false;
        }

        private void MemePlayer_OnMediaEnded(object sender, RoutedEventArgs e)
        {
            this.BeginInvoke((win) =>
            {
                win.MemeFlyout.IsOpen = false;
            });
        }

        private void MemeFlyout_OnIsOpenChanged(object sender, RoutedEventArgs e)
        {
            if (MemeFlyout.IsOpen)
            {
                MemePlayer.Play();
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(5000);
                    MemeFlyout.Invoke(new Action(() =>
                    {
                        if (MemeFlyout.IsOpen)
                        {
                            MemeFlyout.CloseButtonVisibility = Visibility.Visible;
                        }
                    }));

                });
            }
            else
            {
                MemeFlyout.CloseButtonVisibility = Visibility.Hidden;
                MemePlayer.Close();
                GC.Collect();
            }
        }
        private async void SubmitDiscordButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (discordTokenBox.Password != "")
            {
                var progressController = await context.DialogCoordinator.ShowProgressAsync(context, "Token Validation", "Validating discord token", false, new MetroDialogSettings{AnimateShow = true, AnimateHide = false, NegativeButtonText = "OK"});
                progressController.SetIndeterminate();
                try
                {
                    Discord.TokenUtils.ValidateToken(TokenType.Bot, discordTokenBox.Password);
                    progressController.SetMessage("Token validated.");
                    context.Settings.discordToken = JsonConvert.SerializeObject(encryptToken(discordTokenBox.Password));
                    App.handler.Close(); //Anytime we change the token we wanna close the connection. (Will not error if connection already closed)
                    App.handler.Init(
                        decryptToken(JsonConvert.DeserializeObject<byte[]>(context.Settings.discordToken)));
                    progressController.SetProgress(1);
                }
                catch (ArgumentException er)
                {
                    progressController.SetMessage(er.Message);
                    progressController.SetProgress(0);
                    discordTokenBox.Password = decryptToken(JsonConvert.DeserializeObject<byte[]>(context.Settings.discordToken)); //Roll back changes
                }
                progressController.SetCancelable(true);
                progressController.Canceled += delegate(object? o, EventArgs args)
                {
                    progressController.CloseAsync(); //Close the dialog. 
                };
                
            }
            else if (discordTokenBox.Password == string.Empty)
            {
                if (context.Settings.discordToken == "") //If we don't have any password in the config(meaning unencrypted)
                {
                    context.Settings.discordTokenEncrypted = true;
                    context.Settings.discordToken = JsonConvert.SerializeObject(encryptToken(discordTokenBox.Password));
                }

                if (decryptToken(JsonConvert.DeserializeObject<byte[]>(context.Settings.discordToken)) == discordTokenBox.Password) return;
                //No reason to open the box if it didn't change.
                context.Settings.discordToken = JsonConvert.SerializeObject(encryptToken(discordTokenBox.Password));
                App.handler.Close(); //Close connection because token cleared.
                await this.ShowMessageAsync("Success!", "Discord token cleared!", MessageDialogStyle.Affirmative);

            }
        }

        private async void ReloadOffsetsButton_OnClick(object sender, RoutedEventArgs e)
        {
            GameMemReader.getInstance().offMan.refreshLocal();
            await GameMemReader.getInstance().offMan.RefreshIndex();
            GameMemReader.getInstance().CurrentOffsets = GameMemReader.getInstance().offMan
                .FetchForHash(GameMemReader.getInstance().GameHash);
            if (GameMemReader.getInstance().CurrentOffsets is not null)
            {
                WriteConsoleLineFormatted("GameMemReader", Color.Lime, $"Loaded offsets: {GameMemReader.getInstance().CurrentOffsets.Description}");
            }
            else
            {
                WriteConsoleLineFormatted("GameMemReader", Color.Lime, $"No offsets found for: {Color.Aqua.ToTextColor()}{GameMemReader.getInstance().GameHash.ToString()}.");

            }
        }

        private void HelpDiscordButton_OnClick(object sender, RoutedEventArgs e)
        {
            OpenBrowser("https://www.youtube.com/watch?v=jKcEW5qpk8E");
        }


        private void ConsoleTextBox_OnCopying(object sender, DataObjectCopyingEventArgs e)
        {
            e.CancelCommand();
        }

        private void APIServerToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!(sender is ToggleSwitch toggleSwitch))
            {
                return;
            }

            if (config.ApiServer)
            {
                WriteConsoleLineFormatted("APIServer", Color.Brown, "Starting server");
                ServerSocket.instance.Start();
            }
            else
            {
                WriteConsoleLineFormatted("APIServer", Color.Brown, "Stopping server");
                ServerSocket.instance.Stop();
            }
        }


        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            
        }

        private async void AccentColorPicker_OnSelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e)
        {
            if (e.NewValue.HasValue)
            {

                string BaseColor;
                if (context.Settings.DarkMode)
                {
                    BaseColor = ThemeManager.BaseColorDark;
                }
                else
                {
                    BaseColor = ThemeManager.BaseColorLight;
                }

                context.Settings.SelectedAccent = JsonConvert.SerializeObject(e.NewValue.Value);

                Theme newTheme = new Theme(name: "CustomTheme",
                    displayName: "CustomTheme",
                    baseColorScheme: BaseColor,
                    colorScheme: "CustomAccent",
                    primaryAccentColor: e.NewValue.Value,
                    showcaseBrush: new SolidColorBrush(e.NewValue.Value),
                    isRuntimeGenerated: true,
                    isHighContrast: false);

                ThemeManager.Current.ChangeTheme(this, newTheme);
            }
        }


        private async void ResetConfigButton_OnClick(object sender, RoutedEventArgs e)
        {
            var result = await this.ShowMessageAsync("Are you sure?",
                "This action will reset your config.\nThis cannot be undone.",
                MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings{AnimateShow = true, AnimateHide = false});
            if (result == MessageDialogResult.Affirmative)
            {
                var progressBar = await this.context.DialogCoordinator.ShowProgressAsync(context, "Resetting config",
                    "Please wait....", false, new MetroDialogSettings {AnimateHide = false, AnimateShow = false});
                progressBar.Minimum = 0;
                progressBar.Maximum = 1;
                if (File.Exists(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "\\AmongUsCapture\\AmongUsGUI", "Settings.json")))
                {
                    File.Delete(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "\\AmongUsCapture\\AmongUsGUI", "Settings.json"));
                }

                if (File.Exists(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AmongUsCapture", "Settings.json")))
                {
                    File.Delete(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmongUsCapture", "Settings.json"));
                }
                for (int i = 0; i < 100; i++) //Useless loading to make the user think we are doing a big task
                {
                    var currentPercent = i / 100d;
                    progressBar.SetProgress(currentPercent);
                    await Task.Delay(10);
                }

                await progressBar.CloseAsync();
                var selection = await this.ShowMessageAsync("Config reset",
                    "Your config was reset successfully.",
                    MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings{AnimateHide = true, AffirmativeButtonText = "Restart", NegativeButtonText = "Exit"});
                if (selection == MessageDialogResult.Affirmative)
                {
                    IPCAdapter.getInstance().mutex.ReleaseMutex(); //Release the mutex so the other app does not see us. 
                    Process.Start(Process.GetCurrentProcess().MainModule.FileName);  
                    Application.Current.Shutdown(0);
                }
                else
                {

                    Application.Current.Shutdown(0);
                }
                


            }
        }
    }
}
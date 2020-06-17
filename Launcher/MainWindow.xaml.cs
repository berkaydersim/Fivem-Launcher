﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Launcher.Managers;
using Microsoft.Win32;
using Newtonsoft.Json;
// ReSharper disable EmptyGeneralCatchClause

namespace Launcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        public static extern int SendMessage(int hWnd, uint Msg, int wParam, int lParam);


        private const string LauncherUpdateURL = "https://yalc.in/fivem_launcher/update.php";
        private const string ServerUpdateURL = "https://yalc.in/fivem_launcher/guncelle.php";
        private const string ServerCheckURL = "https://yalc.in/fivem_launcher/kontrol.php";
        private const string SteamProxyURL = "https://yalc.in/fivem_launcher/steamProxy.php";
        private const string MessageTitle = "GormYa Launcher";

        private string _steamHex;
        private UpdateObject _globalVariables;
        private readonly bool _isLocal;

        private bool _steamYeniAcildi;

        private readonly DispatcherTimer _timerCheats = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60), IsEnabled = false }; // 60 saniyede bir hile korumasını çalıştır
        private readonly DispatcherTimer _timerSetOnline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25), IsEnabled = false }; // 25 saniyede bir sunucudaki oyuncunun giriş tarihini güncelle
        private readonly DispatcherTimer _timerGetOnlinePlayers = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10), IsEnabled = false }; // 10 saniyede bir sunucudaki oyuncunun giriş tarihini güncelle

        public MainWindow()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1].Equals("-local"))
            {
                _isLocal = true;
            }

            InitializeComponent();
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            MouseLeftButtonDown += delegate { DragMove(); };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FivemManager.KillFivem();

            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("-updated")))
            {
                ShowInformation("Launcher güncellendi!");
                UpdateKontrolEdildi();
            }
            else
            {
                _timerCheats.Tick += CloseCheats;
                _timerSetOnline.Tick += SetOnline;
                _timerGetOnlinePlayers.Tick += GetOnlinePlayers;

                Task.Run(UpdateControl);
            }
        }

        private void ShowError(string message, bool close = true)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(delegate { ShowError(message, close); }); return; }

            Visibility = Visibility.Hidden;
            MessageBox.Show(message, MessageTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            if (close)
            {
                Close();
            }
            else
            {
                Visibility = Visibility.Visible;
            }
        }

        private MessageBoxResult ShowWarning(string message, Visibility visibility = Visibility.Visible)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => ShowWarning(message, visibility));
            }

            Visibility = visibility;
            return MessageBox.Show(message, MessageTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private MessageBoxResult ShowInformation(string message, Visibility visibility = Visibility.Visible)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => ShowInformation(message, visibility));
            }

            Visibility = visibility;
            return MessageBox.Show(message, MessageTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private MessageBoxResult ShowQuestion(string message, MessageBoxButton messageBoxButton = MessageBoxButton.YesNo, Visibility visibility = Visibility.Visible)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => ShowQuestion(message, messageBoxButton, visibility));
            }

            Visibility = visibility;
            return MessageBox.Show(message, MessageTitle, messageBoxButton, MessageBoxImage.Question);
        }

        private void Copy3DMapFiles()
        {
            // 3d harita dosyalarini kopyala
            Task.Run(() =>
            {
                var fivemFolder = FivemManager.GetFivemFolder();

                try
                {
                    File.WriteAllBytes($"{fivemFolder}mapzoomdata.meta", Properties.Resources.mapzoomdata);
                    File.WriteAllBytes($"{fivemFolder}pausemenu.xml", Properties.Resources.pausemenu_xml);
                    return true;
                }
                catch
                {
                    return false;
                }
            }).ContinueWith(task =>
            {
                if (!task.Result)
                {
                    ShowWarning("Harita dosyaları kopyalanamadı.");
                }
            });
        }

        private async Task UpdateControl()
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            var exePath = Assembly.GetExecutingAssembly().Location;

            var updater = new UpdateManager(LauncherUpdateURL, exePath);

            _globalVariables = await updater.CheckUpdate();

            if (_globalVariables == null)
            {
                ShowError("Launcher bilgilerini okuyamadım. İnternet bağlantınızda veya sunucumuzda sorun olabilir.");
            }
            else
            {
                if (_globalVariables.Version.Equals(currentVersion))
                {
                    UpdateKontrolEdildi();
                    return;
                }

                var isDownloaded = await updater.DownloadUpdate();
                if (!isDownloaded)
                {
                    ShowInformation("Güncelleme kontrol edilirken bir hata oluştu.");
                    UpdateKontrolEdildi();
                    return;
                }

                ShowInformation("Launcher güncellenecektir. Kapatılıp açılırken lütfen bekleyiniz...", Visibility.Hidden);
                updater.InstallUpdate();
            }
        }

        private void UpdateKontrolEdildi()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(UpdateKontrolEdildi); return; }

            Copy3DMapFiles(); // 3D haritayı fivem klasörüne kopyala

            GetSteamHex().ContinueWith(RenderUI); // Butonların ve online sayısının görünürlüğünü ayarla

            CloseCheats(null, null); // Çalışan hile programı var mı kontrol et

            _timerCheats.Start();

            if (!string.IsNullOrEmpty(_globalVariables.ServerCode))
            {
                _timerGetOnlinePlayers.Start();
            }
        }

        private void RenderUI(Task<string> task)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(delegate { RenderUI(task); }); return; }

            if (string.IsNullOrEmpty(task.Result)) { ShowError("Steam bilgileri okunurken hata oluştu."); }

            // Discord boş değilse butonunu göster
            if (!string.IsNullOrEmpty(_globalVariables?.Discord))
            {
                BtnDiscord.Visibility = Visibility.Visible;
            }

            // TS3 boş değilse butonunu göster
            if (!string.IsNullOrEmpty(_globalVariables?.Teamspeak3))
            {
                BtnTeamspeak.Visibility = Visibility.Visible;
            }

            // Server code boş değilse online sayısını göster
            if (!string.IsNullOrEmpty(_globalVariables?.ServerCode))
            {
                LblOnline.Visibility = Visibility.Visible;
            }

            // Server boş değilse butonunu göster
            if (!string.IsNullOrEmpty(_globalVariables?.Server))
            {
                BtnLaunch.Visibility = Visibility.Visible;
            }
        }

        private async Task<string> GetSteamHex()
        {
            if (!SteamManager.IsRunning())
            {
                var response = ShowQuestion($"Steam açık değil ve bu şekilde sunucuya bağlanamazsın.{Environment.NewLine}Açmamı ister misin?");
                if (response == MessageBoxResult.Yes)
                {
                    if (SteamManager.RunSteam())
                    {
                        _steamYeniAcildi = true;
                    }
                    else
                    {
                        _steamHex = null;
                        ShowError("Steam'i açamadım. Sen benim yerime açıp, tekrar beni çalıştırabilirsin :)");
                        return _steamHex;
                    }
                }
                else
                {
                    _steamHex = null;
                    ShowError("Bir sonraki sefere görüşmek üzere :)");
                    return _steamHex;
                }
            }

            var steamIdOkumaDenemesi = 0;
        steamID3Oku:
            var steamID3 = SteamManager.GetSteamID3();
            if (string.IsNullOrEmpty(steamID3) || steamID3.Equals("0"))
            {
                if (_steamYeniAcildi)
                {
                    if (steamIdOkumaDenemesi <= 120) // steam açılmasını 120 saniyeye kadar bekle bekle
                    {
                        steamIdOkumaDenemesi++;
                        Thread.Sleep(1000);
                        goto steamID3Oku;
                    }

                    _steamHex = null;
                    ShowError("Oyuna bağlanabilmek için Steam girişi yapmış olmalısın!");
                    return _steamHex;
                }

                _steamHex = null;
                ShowError("Oyuna bağlanabilmek için Steam girişi yapmış olmalısın!");
                return _steamHex;
            }

            var steamID64 = SteamManager.ConvertSteamID64(steamID3);
            if (string.IsNullOrEmpty(steamID64) || steamID64.Equals("0"))
            {
                _steamHex = null;
                ShowError("Steam bilgilerine ulaşamadım. Lütfen daha sonra tekrar dene.");
                return _steamHex;
            }

            // Steam api'den kullanıcı bilgilerini çek ve kontrol et
            var steamProfile = await SteamManager.GetSteamProfile(SteamProxyURL, steamID64);
            if (steamProfile == null || string.IsNullOrEmpty(steamProfile.Personaname))
            {
                _steamHex = null;
                ShowError("Steam bilgilerinizi okuyamadık!");
                return _steamHex;
            }

            _steamHex = SteamManager.ConvertSteamIDHex(steamID64);
            return _steamHex;
        }

        private void CloseCheats(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                var controlledProcess = 0;
                List<string> killedProcess = new List<string>();

                var processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    var processName = process.ProcessName;
                    var windowTitle = process.MainWindowTitle;


                    if (!string.IsNullOrWhiteSpace(windowTitle))
                    {
                        if (_globalVariables.Cheats.Any(s => processName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) || _globalVariables.Cheats.Any(s => windowTitle.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            killedProcess.Add(process.ProcessName);
                            try { process.Kill(); } catch { try { SendMessage(process.MainWindowHandle.ToInt32(), 0x0112, 0xF060, 0); } catch { } }
                        }
                        else { controlledProcess++; }
                    }
                    else
                    {
                        if (_globalVariables.Cheats.Any(s => processName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            killedProcess.Add(process.ProcessName);
                            try { process.Kill(); } catch { try { SendMessage(process.MainWindowHandle.ToInt32(), 0x0112, 0xF060, 0); } catch { } }
                        }
                        else { controlledProcess++; }
                    }
                }

                if (killedProcess.Any())
                {
                    // TODO: Hile report olacak
                    FivemManager.KillFivem();
                    ShowError("Bilgisayarınızda hile programı çalıştığı tespit edildi.");
                    Console.WriteLine(string.Join(",", killedProcess));
                }

                if (controlledProcess == 0)
                {
                    FivemManager.KillFivem();
                    ShowError("Bilgisayarınız anti-hile taramasına izin vermiyor.");
                }
            });
        }

        private void SetOnline(object sender, EventArgs e)
        {
            // Oyundan disconnect olmuş mu kontrol et, disconnect olmamışsa son girişi güncelle
            Task.Run(() => LauncherAPIManager.GetStatus(ServerCheckURL, _steamHex)).ContinueWith(getTask =>
            {
                var status = getTask.Result;

                if (string.IsNullOrEmpty(status)) return;

                if (status == "-4")
                {
                    FivemManager.KillFivem();
                }
                else
                {
                    var task = LauncherAPIManager.SetStatus(ServerUpdateURL, _steamHex, status);
                }
            });
        }

        private void GetOnlinePlayers(object sender, EventArgs e)
        {
            // Online sayısını güncelle
            Task.Run(() =>
            {
                try
                {
                    using (var webClient = new WebClient())
                    {
                        webClient.DownloadStringTaskAsync(new Uri($"https://servers-frontend.fivem.net/api/servers/single/{_globalVariables.ServerCode}"))
                            .ContinueWith(task =>
                            {
                                var obj = JsonConvert.DeserializeObject<FivemApi>(task.Result);
                                Dispatcher.Invoke(delegate { LblOnline.Content = $"Online: {obj.Data.Clients}"; });
                            });
                    }
                }
                catch
                {
                    // ignored
                }
            });
        }

        private void btnDiscord_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(_globalVariables.Discord);
        }

        private void btnTeamspeak_Click(object sender, RoutedEventArgs e)
        {
            Process.Start($"ts3server://{_globalVariables.Teamspeak3}");
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnLaunch_Click(object sender, RoutedEventArgs e)
        {
            FivemManager.KillFivem();

            Task.Run(() => LauncherAPIManager.SetStatus(ServerUpdateURL, _steamHex, "1")).ContinueWith(task =>
            {
                if (task.Result == "1")
                {
                    GetSteamHex().ContinueWith(StartFivem);
                }
                else
                {
                    MessageBox.Show("Sunucu kaydınız yapılamadı. Daha sonra tekrar deneyin.");
                }
            });
        }

        private void StartFivem(Task<string> task)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(delegate { StartFivem(task); }); return; }

            if (string.IsNullOrEmpty(task.Result)) { ShowError("Steam bilgileri okunurken hata oluştu."); }

            BtnLaunch.IsEnabled = false;

            if (!_timerSetOnline.IsEnabled) _timerSetOnline.Start();

            if (_timerGetOnlinePlayers.IsEnabled) _timerGetOnlinePlayers.Stop();

            Task.Run(() =>
            {
                var process = Process.Start($"fivem://connect/{(_isLocal ? "localhost:30120" : _globalVariables.Server)}", "-gormya");
                process?.WaitForExit();
            }).ContinueWith(FivemStopped);
        }

        private void FivemStopped(Task task)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(delegate { FivemStopped(task); }); return; }

            Task.Run(() => LauncherAPIManager.SetStatus(ServerUpdateURL, _steamHex, "0"));

            BtnLaunch.IsEnabled = true;

            if (_timerSetOnline.IsEnabled) _timerSetOnline.Stop();

            if (!string.IsNullOrEmpty(_globalVariables.ServerCode) && !_timerGetOnlinePlayers.IsEnabled) _timerGetOnlinePlayers.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!BtnLaunch.IsEnabled)
            {
                e.Cancel = MessageBox.Show($"Launcher kapatırsanız, Fivem de kapanacak.{Environment.NewLine}Emin misiniz?", MessageTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes;
                return;
            }

            FivemManager.KillFivem();
        }
    }
}

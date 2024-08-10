/*
DS4Updater
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Principal;
using System.Windows;
using System.Windows.Shell;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace DS4Updater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string CUSTOM_EXE_CONFIG_FILENAME = "custom_exe_name.txt";
        //WebClient wc = new WebClient(), subwc = new WebClient();
        private HttpClient wc = new HttpClient();
        protected string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DS4Windows");
        string exepath = AppContext.BaseDirectory;
        string version = "0", newversion = "0";
        bool downloading = false;
        private int round = 1;
        public bool downloadLang = false;
        private bool backup;
        private string outputUpdatePath = "";
        private string updatesFolder = "";
        public bool autoLaunchDS4W = false;
        public bool forceLaunchDS4WUser = false;
        internal string arch = Environment.Is64BitProcess ? "x64" : "x86";
        private string custom_exe_name_path;
        public string CustomExeNamePath { get => custom_exe_name_path; }

        [DllImport("Shell32.dll")]
        private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)]Guid rfid, uint dwFlags, IntPtr hToken,
        out IntPtr ppszPath);

        public bool AdminNeeded()
        {
            try
            {
                File.WriteAllText(exepath + "\\test.txt", "test");
                // Add a small sleep period as a pre-caution
                Thread.Sleep(20);
                File.Delete(exepath + "\\test.txt");
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            if (File.Exists(exepath + "\\DS4Windows.exe"))
                version = FileVersionInfo.GetVersionInfo(exepath + "\\DS4Windows.exe").FileVersion;

            if (AdminNeeded())
                label1.Content = "Please re-run with admin rights";
            else
            {
                custom_exe_name_path = Path.Combine(exepath, CUSTOM_EXE_CONFIG_FILENAME);

                try
                {
                    string[] files = Directory.GetFiles(exepath);

                    for (int i = 0, arlen = files.Length; i < arlen; i++)
                    {
                        string tempFile = Path.GetFileName(files[i]);
                        if (new Regex(@"DS4Windows_[\w.]+_\w+.zip").IsMatch(tempFile))
                        {
                            File.Delete(files[i]);
                        }
                    }

                    if (Directory.Exists(exepath + "\\Update Files"))
                        Directory.Delete(exepath + "\\Update Files", true);

                    if (!Directory.Exists(Path.Combine(exepath, "Updates")))
                        Directory.CreateDirectory(Path.Combine(exepath, "Updates"));

                    updatesFolder = Path.Combine(exepath, "Updates");
                }
                catch (IOException) { label1.Content = "Cannot save download at this time"; return; }

                if (File.Exists(exepath + "\\Profiles.xml"))
                    path = exepath;

                if (File.Exists(path + "\\version.txt"))
                {
                    newversion = File.ReadAllText(path + "\\version.txt");
                    newversion = newversion.Trim();
                }
                else if (File.Exists(exepath + "\\version.txt"))
                {
                    newversion = File.ReadAllText(exepath + "\\version.txt");
                    newversion = newversion.Trim();
                }
                else
                {
                    StartVersionFileDownload();
                }

                if (!downloading && version.Replace(',', '.').CompareTo(newversion) != 0)
                {
                    Uri url = new Uri($"https://github.com/schmaldeo/DS4Windows/releases/download/v{newversion}/DS4Windows_{newversion}_{arch}.zip");
                    sw.Start();
                    outputUpdatePath = Path.Combine(updatesFolder, $"DS4Windows_{newversion}_{arch}.zip");
                    StartAppArchiveDownload(url, outputUpdatePath);
                }
                else if (!downloading)
                {
                    label1.Content = "DS4Windows is up to date";
                    try
                    {
                        File.Delete(path + "\\version.txt");
                        File.Delete(exepath + "\\version.txt");
                    }
                    catch { }
                    btnOpenDS4.IsEnabled = true;
                }
            }
        }

        private void StartAppArchiveDownload(Uri url, string outputUpdatePath)
        {
            Task.Run(async () =>
            {
                try
                {
                    bool success = false;
                    using (var downloadStream = new FileStream(outputUpdatePath, FileMode.CreateNew))
                    {
                        using HttpResponseMessage response = await wc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        long contentLen = response.Content.Headers.ContentLength ?? 0;
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        {
                            byte[] buffer = new byte[16384];
                            int bytesRead = 0;
                            long totalBytesRead = 0;
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                            {
                                await downloadStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                                totalBytesRead += bytesRead;
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    wc_DownloadProgressChanged(new CopyProgress(totalBytesRead, contentLen));
                                });
                            }

                            if (downloadStream.CanSeek) downloadStream.Position = 0;
                        }

                        success = response.IsSuccessStatusCode;
                        response.EnsureSuccessStatusCode();
                    }

                    if (success)
                    {
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            wc_DownloadFileCompleted();
                        });
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            label1.Content = "Could not download update";
                        });
                    }

                    //wc.DownloadFileAsync(url, outputUpdatePath);
                }
                catch (HttpRequestException)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        label1.Content = "Could not download update";
                    });
                }
                catch (Exception e) { label1.Content = e.Message; }
                //wc.DownloadFileCompleted += wc_DownloadFileCompleted;
                //wc.DownloadProgressChanged += wc_DownloadProgressChanged;
            });
        }

        private void StartVersionFileDownload()
        {
            Uri urlv = new Uri("https://raw.githubusercontent.com/schmaldeo/DS4Windows/master/DS4Windows/newest.txt");
            //Sorry other devs, gonna have to find your own server
            downloading = true;

            label1.Content = "Getting Update info";
            Task.Run(async () =>
            {
                try
                {
                    bool success = false;
                    using HttpResponseMessage response = await wc.GetAsync(urlv);
                    response.EnsureSuccessStatusCode();
                    success = response.IsSuccessStatusCode;

                    if (success)
                    {
                        string verPath = Path.Combine(exepath, "version.txt");
                        using (FileStream fs = new FileStream(verPath, FileMode.CreateNew))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        subwc_DownloadFileCompleted();
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            label1.Content = "Could not download update";
                        });
                    }
                    //subwc.DownloadFileAsync(urlv, exepath + "\\version.txt");
                    //subwc.DownloadFileCompleted += subwc_DownloadFileCompleted;
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine(e);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        label1.Content = "Could not download update";
                    });
                }
            });
        }

        private void subwc_DownloadFileCompleted()
        {
            newversion = File.ReadAllText(Path.Combine(exepath,  "version.txt"));
            newversion = newversion.Trim();
            File.Delete(Path.Combine(exepath, "version.txt"));
            if (version.Replace(',', '.').CompareTo(newversion) != 0)
            {
                Uri url = new Uri($"https://github.com/schmaldeo/DS4Windows/releases/download/v{newversion}/DS4Windows_{newversion}_{arch}.zip");
                sw.Start();
                outputUpdatePath = Path.Combine(updatesFolder, $"DS4Windows_{newversion}_{arch}.zip");

                //wc.DownloadFileAsync(url, outputUpdatePath);
                //Task.Run(async () =>
                Func<Task> currentTask = async () =>
                {
                    try
                    {
                        bool success = false;
                        using (var downloadStream = new FileStream(outputUpdatePath, FileMode.CreateNew))
                        {
                            using HttpResponseMessage response =
                                await wc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                            long contentLen = response.Content.Headers.ContentLength ?? 0;
                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            {
                                byte[] buffer = new byte[16384];
                                int bytesRead = 0;
                                long totalBytesRead = 0;
                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)
                                           .ConfigureAwait(false)) != 0)
                                {
                                    await downloadStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                                    totalBytesRead += bytesRead;
                                    Application.Current.Dispatcher.BeginInvoke(() =>
                                    {
                                        wc_DownloadProgressChanged(
                                            new CopyProgress(totalBytesRead, contentLen));
                                    });
                                }

                                if (downloadStream.CanSeek) downloadStream.Position = 0;
                            }

                            success = response.IsSuccessStatusCode;
                            response.EnsureSuccessStatusCode();
                        }

                        if (success)
                        {
                            Application.Current.Dispatcher.BeginInvoke(() => { wc_DownloadFileCompleted(); });
                        }
                    }
                    catch (Exception ec)
                    {
                        Application.Current.Dispatcher.BeginInvoke(() => { label1.Content = ec.Message; });
                    }
                    //});
                };
                currentTask?.Invoke();
                //wc.DownloadFileCompleted += wc_DownloadFileCompleted;
                //wc.DownloadProgressChanged += wc_DownloadProgressChanged;
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    label1.Content = "DS4Windows is up to date";
                });
                
                try
                {
                    File.Delete(Path.Combine(path, "version.txt"));
                    File.Delete(Path.Combine(exepath + "version.txt"));
                }
                catch { }

                if (autoLaunchDS4W)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        label1.Content = "Launching DS4Windows soon";
                        btnOpenDS4.IsEnabled = false;
                    });

                    Task.Delay(5000).ContinueWith((t) =>
                    {
                        PrepareAutoOpenDS4();
                    });
                }
                else
                {
                    btnOpenDS4.IsEnabled = true;
                }
            }
        }

        private bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        Stopwatch sw = new Stopwatch();

        private void wc_DownloadProgressChanged(CopyProgress e)
        {
            label2.Opacity = 1;
            double speed = e.BytesTransferred / sw.Elapsed.TotalSeconds;
            double timeleft = (e.ExpectedBytes - e.BytesTransferred) / speed;
            if (timeleft > 3660)
                label2.Content = (int)timeleft / 3600 + "h left";
            else if (timeleft > 90)
                label2.Content = (int)timeleft / 60 + "m left";
            else
                label2.Content = (int)timeleft + "s left";

            UpdaterBar.Value = e.PercentComplete * 100.0;
            TaskbarItemInfo.ProgressValue = UpdaterBar.Value / 106d;
            string convertedrev, convertedtotal;
            if (e.BytesTransferred > 1024 * 1024 * 5) convertedrev = (int)(e.BytesTransferred / 1024d / 1024d) + "MB";
            else convertedrev = (int)(e.BytesTransferred / 1024d) + "kB";

            if (e.ExpectedBytes > 1024 * 1024 * 5) convertedtotal = (int)(e.ExpectedBytes / 1024d / 1024d) + "MB";
            else convertedtotal = (int)(e.ExpectedBytes / 1024d) + "kB";

            if (round == 1) label1.Content = "Downloading update: " + convertedrev + " / " + convertedtotal;
            else label1.Content = "Downloading Language Pack: " + convertedrev + " / " + convertedtotal;
        }

        //private void wc_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        private async void wc_DownloadFileCompleted()
        {
            sw.Reset();
            string lang = CultureInfo.CurrentCulture.ToString();

            if (new FileInfo(outputUpdatePath).Length > 0)
            {
                Process[] processes = Process.GetProcessesByName("DS4Windows");
                label1.Content = "Download Complete";
                if (processes.Length > 0)
                {
                    if (MessageBox.Show("It will be closed to continue this update.", "DS4Windows is still running", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
                    {
                        label1.Content = "Terminating DS4Windows";
                        foreach (Process p in processes)
                        {
                            if (!p.HasExited)
                            {
                                try
                                {
                                    p.Kill();
                                }
                                catch
                                {
                                    MessageBox.Show("Failed to close DS4Windows. Cannot continue update. Please terminate DS4Windows and run DS4Updater again.");
                                    this.Close();
                                    return;
                                }
                            }
                        }

                        System.Threading.Thread.Sleep(5000);
                    }
                    else
                    {
                        this.Close();
                        return;
                    }
                }

                while (processes.Length > 0)
                {
                    label1.Content = "Waiting for DS4Windows to close";
                    processes = Process.GetProcessesByName("DS4Windows");
                    System.Threading.Thread.Sleep(200);
                }

                // Need to check for presense of HidGuardHelper
                processes = Process.GetProcessesByName("HidGuardHelper");
                if (processes.Length > 0)
                {
                    label1.Content = "Waiting for HidGuardHelper to close";
                    System.Threading.Thread.Sleep(5000);

                    processes = Process.GetProcessesByName("HidGuardHelper");
                    if (processes.Length > 0)
                    {
                        MessageBox.Show("HidGuardHelper will not close. Cannot continue update. Please terminate HidGuardHelper and run DS4Updater again.");
                        this.Close();
                        return;
                    }
                }

                label2.Opacity = 0;
                label1.Content = "Deleting old files";
                UpdaterBar.Value = 102;
                TaskbarItemInfo.ProgressValue = UpdaterBar.Value / 106d;

                string libsPath = Path.Combine(exepath, "libs");
                string oldLibsPath = Path.Combine(exepath, "oldlibs");

                // Grab relative file paths to DLL files in the current install
                string[] oldDLLFiles = Directory.GetDirectories(exepath, "*.dll", SearchOption.AllDirectories);
                for (int i = oldDLLFiles.Length - 1; i >= 0; i--)
                {
                    oldDLLFiles[i] = oldDLLFiles[i].Replace($"{exepath}", "");
                }

                try
                {
                    // Temporarily move existing libs folder
                    if (Directory.Exists(libsPath))
                    {
                        Directory.Move(libsPath, oldLibsPath);
                    }

                    string[] checkFiles = new string[]
                    {
                        exepath + "\\DS4Windows.exe",
                        exepath + "\\DS4Tool.exe",
                        exepath + "\\DS4Control.dll",
                        exepath + "\\DS4Library.dll",
                        exepath + "\\HidLibrary.dll",
                    };

                    foreach(string checkFile in checkFiles)
                    {
                        if (File.Exists(checkFile))
                        {
                            File.Delete(checkFile);
                        }
                    }

                    string updateFilesDir = exepath + "\\Update Files";
                    if (Directory.Exists(updateFilesDir))
                    {
                        Directory.Delete(updateFilesDir);
                    }

                    string[] updatefiles = Directory.GetFiles(exepath);
                    for (int i = 0, arlen = updatefiles.Length; i < arlen; i++)
                    {
                        if (Path.GetExtension(updatefiles[i]) == ".ds4w" && File.Exists(updatefiles[i]))
                            File.Delete(updatefiles[i]);
                    }
                }
                catch { }

                label1.Content = "Installing new files";
                UpdaterBar.Value = 104;
                TaskbarItemInfo.ProgressValue = UpdaterBar.Value / 106d;

                try
                {
                    Directory.CreateDirectory(exepath + "\\Update Files");
                    ZipFile.ExtractToDirectory(outputUpdatePath, exepath + "\\Update Files");
                }
                catch (IOException) { }

                try
                {
                    File.Delete(exepath + "\\version.txt");
                    File.Delete(path + "\\version.txt");
                }
                catch { }

                // Add small sleep timer here as a pre-caution
                Thread.Sleep(20);

                string[] directories = Directory.GetDirectories(exepath + "\\Update Files\\DS4Windows", "*", SearchOption.AllDirectories);
                for (int i = directories.Length - 1; i >= 0; i--)
                {
                    string relativePath = directories[i].Replace($"{exepath}\\Update Files\\DS4Windows\\", "");
                    string tempDestPath = Path.Combine(exepath, relativePath);
                    if (!Directory.Exists(tempDestPath))
                    {
                        Directory.CreateDirectory(tempDestPath);
                    }
                }

                // Grab relative file paths to DLL files in the newer install
                string[] newDLLFiles = Directory.GetFiles(exepath + "\\Update Files\\DS4Windows", "*.dll", SearchOption.AllDirectories);
                for (int i = newDLLFiles.Length - 1; i >= 0; i--)
                {
                    newDLLFiles[i] = newDLLFiles[i].Replace($"{exepath}\\Update Files\\DS4Windows\\", "");
                }

                string[] files = Directory.GetFiles(exepath + "\\Update Files\\DS4Windows", "*", SearchOption.AllDirectories);
                for (int i = files.Length - 1; i >= 0; i--)
                {
                    if (Path.GetFileNameWithoutExtension(files[i]) != "DS4Updater")
                    {
                        string relativePath = files[i].Replace($"{exepath}\\Update Files\\DS4Windows\\", "");
                        string tempDestPath = Path.Combine(exepath, relativePath);
                        //string tempDestPath = $"{exepath}\\{Path.GetFileName(files[i])}";
                        if (File.Exists(tempDestPath))
                        {
                            File.Delete(tempDestPath);
                        }

                        File.Move(files[i], tempDestPath);
                    }
                }

                // Delete old libs folder
                if (Directory.Exists(oldLibsPath))
                {
                    Directory.Delete(oldLibsPath, true);
                }

                // Remove unused DLLs (in main app folder) from previous install
                string[] excludedDLLs = oldDLLFiles.Except(newDLLFiles).ToArray();
                foreach (string dllFile in excludedDLLs)
                {
                    if (File.Exists(dllFile))
                    {
                        File.Delete(dllFile);
                    }
                }

                string ds4winversion = FileVersionInfo.GetVersionInfo(exepath + "\\DS4Windows.exe").FileVersion;
                if ((File.Exists(exepath + "\\DS4Windows.exe") || File.Exists(exepath + "\\DS4Tool.exe")) &&
                    ds4winversion == newversion.Trim())
                {
                    //File.Delete(exepath + $"\\DS4Windows_{newversion}_{arch}.zip");
                    //File.Delete(exepath + "\\" + lang + ".zip");
                    label1.Content = $"DS4Windows has been updated to v{newversion}";
                }
                else if (File.Exists(exepath + "\\DS4Windows.exe") || File.Exists(exepath + "\\DS4Tool.exe"))
                {
                    label1.Content = "Could not replace DS4Windows, please manually unzip";
                }
                else
                    label1.Content = "Could not unpack zip, please manually unzip";

                // Check for custom exe name setting
                string custom_exe_name_path = Path.Combine(exepath, CUSTOM_EXE_CONFIG_FILENAME);
                bool fakeExeFileExists = File.Exists(custom_exe_name_path);
                if (fakeExeFileExists)
                {
                    string fake_exe_name = File.ReadAllText(custom_exe_name_path).Trim();
                    bool valid = !string.IsNullOrEmpty(fake_exe_name) && !(fake_exe_name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0);
                    // Attempt to copy executable and assembly config file
                    if (valid)
                    {
                        string current_exe_location = Path.Combine(exepath, "DS4Windows.exe");
                        string current_conf_file_path = Path.Combine(exepath, "DS4Windows.runtimeconfig.json");
                        string current_deps_file_path = Path.Combine(exepath, "DS4Windows.deps.json");

                        string fake_exe_file = Path.Combine(exepath, $"{fake_exe_name}.exe");
                        string fake_conf_file = Path.Combine(exepath, $"{fake_exe_name}.runtimeconfig.json");
                        string fake_deps_file = Path.Combine(exepath, $"{fake_exe_name}.deps.json");

                        File.Copy(current_exe_location, fake_exe_file, true); // Copy exe file

                        // Copy needed app config and deps files
                        File.Copy(current_conf_file_path, fake_conf_file, true);
                        File.Copy(current_deps_file_path, fake_deps_file, true);
                    }
                }

                UpdaterBar.Value = 106;
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;

                if (autoLaunchDS4W)
                {
                    label1.Content = "Launching DS4Windows soon";
                    btnOpenDS4.IsEnabled = false;
                    Task.Delay(5000).ContinueWith((t) =>
                    {
                        PrepareAutoOpenDS4();
                    });
                }
                else
                {
                    btnOpenDS4.IsEnabled = true;
                }
            }
            else if (!backup)
            {
                Uri url = new Uri($"https://github.com/schmaldeo/DS4Windows/releases/download/v{newversion}/DS4Windows_{newversion}_{arch}.zip");

                sw.Start();
                outputUpdatePath = Path.Combine(updatesFolder, $"DS4Windows_{newversion}_{arch}.zip");
                try
                {
                    bool success = false;
                    using (var downloadStream = new FileStream(outputUpdatePath, FileMode.CreateNew))
                    {
                        using HttpResponseMessage response = await wc.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        success = response.IsSuccessStatusCode;
                        if (success)
                        {
                            await response.Content.CopyToAsync(downloadStream);
                        }
                    }
                    //wc.DownloadFileAsync(url, outputUpdatePath);
                }
                catch (Exception ex) { label1.Content = ex.Message; }
                backup = true;
            }
            else
            {
                label1.Content = "Could not download update";
                try
                {
                    File.Delete(exepath + "\\version.txt");
                    File.Delete(path + "\\version.txt");
                }
                catch { }
                btnOpenDS4.IsEnabled = true;
            }
        }

        private void BtnChangelog_Click(object sender, RoutedEventArgs e)
        {
            // TODO change
            ProcessStartInfo startInfo = new ProcessStartInfo("https://docs.google.com/document/d/1CovpH08fbPSXrC6TmEprzgPwCe0tTjQ_HTFfDotpmxk/edit?usp=sharing");
            startInfo.UseShellExecute = true;
            try
            {
                using (Process tempProc = Process.Start(startInfo))
                {
                }
            }
            catch { }
        }

        private void BtnOpenDS4_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(exepath + "\\DS4Windows.exe"))
                Process.Start(exepath + "\\DS4Windows.exe");
            else
                Process.Start(exepath);

            App.openingDS4W = true;
            this.Close();
        }

        private void PrepareAutoOpenDS4()
        {
            App.openingDS4W = true;
            Dispatcher.BeginInvoke((Action)(() =>
            {
                this.Close();
            }));
        }
    }
}


﻿#region Licence

// Distributed under MIT License
// ===========================================================
// 
// digiCamControl - DSLR camera remote control open source software
// Copyright (C) 2014 Duka Istvan
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
// MERCHANTABILITY,FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY 
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
// THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#endregion

#region

using System;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CameraControl.Classes;
using CameraControl.Core;
using CameraControl.Core.Classes;
using CameraControl.Core.Interfaces;
using CameraControl.Core.Translation;
using CameraControl.Core.Wpf;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using CameraControl.Layouts;
using CameraControl.ViewModel;
using CameraControl.windows;
using Hardcodet.Wpf.TaskbarNotification;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using EditSession = CameraControl.windows.EditSession;
using FileInfo = System.IO.FileInfo;
using HelpProvider = CameraControl.Classes.HelpProvider;
using MessageBox = System.Windows.MessageBox;
//using MessageBox = System.Windows.Forms.MessageBox;
using Path = System.IO.Path;
using Timer = System.Timers.Timer;

#endregion

namespace CameraControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow :  IMainWindowPlugin, INotifyPropertyChanged
    {
        public string DisplayName { get; set; }

        private object _locker = new object();
        private FileItem _selectedItem = null;
        private Timer _selectiontimer = new Timer(4000);
        private DateTime _lastLoadTime = DateTime.Now;

        private bool _sortCameraOreder = true;

        public RelayCommand<AutoExportPluginConfig> ConfigurePluginCommand { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow" /> class.
        /// </summary>
        public MainWindow()
        {
            DisplayName = "Default";
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (sender1, args) => this.Close()));

            SelectPresetCommand = new RelayCommand<CameraPreset>(SelectPreset);
            DeletePresetCommand = new RelayCommand<CameraPreset>(DeletePreset,
                (o) => ServiceProvider.Settings.CameraPresets.Count > 0);
            LoadInAllPresetCommand = new RelayCommand<CameraPreset>(LoadInAllPreset);
            VerifyPresetCommand = new RelayCommand<CameraPreset>(VerifyPreset);

            ExecuteExportPluginCommand = new RelayCommand<IExportPlugin>(ExecuteExportPlugin);
            ExecuteToolPluginCommand = new RelayCommand<IToolPlugin>(ExecuteToolPlugin);
            ConfigurePluginCommand = new RelayCommand<AutoExportPluginConfig>(ConfigurePlugin);

            InitializeComponent();


            if (!string.IsNullOrEmpty(ServiceProvider.Branding.ApplicationTitle))
            {
                Title = ServiceProvider.Branding.ApplicationTitle;
            }
            if (!string.IsNullOrEmpty(ServiceProvider.Branding.LogoImage) &&
                File.Exists(ServiceProvider.Branding.LogoImage))
            {
                BitmapImage bi = new BitmapImage();
                // BitmapImage.UriSource must be in a BeginInit/EndInit block.
                bi.BeginInit();
                bi.UriSource = new Uri(ServiceProvider.Branding.LogoImage);
                bi.EndInit();
                Icon = bi;
            }
            _selectiontimer.Elapsed += _selectiontimer_Elapsed;
            _selectiontimer.AutoReset = false;
            ServiceProvider.WindowsManager.Event += WindowsManager_Event;
        }

        private void ConfigurePlugin(AutoExportPluginConfig plugin)
        {
            var pluginEdit = new AutoExportPluginEdit
            {
                DataContext = new AutoExportPluginEditViewModel(plugin),
                Owner = this
            };
            pluginEdit.ShowDialog();
        }

        private void WindowsManager_Event(string cmd, object o)
        {
            switch (cmd)
            {
                case CmdConsts.SortCameras:
                    SortCameras();
                    break;
                case CmdConsts.All_Minimize:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        WindowState = WindowState.Minimized;
                    }));
                    break;
            }
        }

        private void LoadInAllPreset(CameraPreset preset)
        {
            if (preset == null)
                return;
            var dlg = new ProgressWindow();
            dlg.Show();
            try
            {
                int i = 0;
                dlg.MaxValue = ServiceProvider.DeviceManager.ConnectedDevices.Count;
                foreach (ICameraDevice connectedDevice in ServiceProvider.DeviceManager.ConnectedDevices)
                {
                    if (connectedDevice == null || !connectedDevice.IsConnected)
                        continue;
                    try
                    {

                        dlg.Label = connectedDevice.DisplayName;
                        dlg.Progress = i;
                        i++;
                        preset.Set(connectedDevice);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("Unable to set property ", exception);
                    }
                    Thread.Sleep(250);
                }
            }
            catch (Exception exception)
            {
                Log.Error("Unable to set property ", exception);
            }
            dlg.Hide();
        }

        private void VerifyPreset(CameraPreset preset)
        {
            if (preset == null)
                return;
            var dlg = new ProgressWindow();
            dlg.Show();
            try
            {
                int i = 0;
                dlg.MaxValue = ServiceProvider.DeviceManager.ConnectedDevices.Count;
                foreach (ICameraDevice connectedDevice in ServiceProvider.DeviceManager.ConnectedDevices)
                {
                    if (connectedDevice == null || !connectedDevice.IsConnected)
                        continue;
                    try
                    {
                        dlg.Label = connectedDevice.DisplayName;
                        dlg.Progress = i;
                        i++;
                        preset.Verify(connectedDevice);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("Unable to set property ", exception);
                    }
                    Thread.Sleep(250);
                }
            }
            catch (Exception exception)
            {
                Log.Error("Unable to set property ", exception);
            }
            dlg.Hide();
        }

        private void DeletePreset(CameraPreset obj)
        {
            if (obj == null)
                return;
            ServiceProvider.Settings.CameraPresets.Remove(obj);
        }

        private void _selectiontimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_selectedItem != null)
                ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.Select_Image, _selectedItem);
        }

        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //WiaManager = new WIAManager();
            //ServiceProvider.Settings.Manager = WiaManager;
            ServiceProvider.DeviceManager.PhotoCaptured += DeviceManager_PhotoCaptured;

            DataContext = ServiceProvider.Settings;
            ServiceProvider.DeviceManager.CameraSelected += DeviceManager_CameraSelected;
            ServiceProvider.DeviceManager.CameraConnected += DeviceManager_CameraConnected;
            ServiceProvider.DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;
            SetLayout(ServiceProvider.Settings.SelectedLayout);
            //var thread = new Thread(CheckForUpdate);
            //thread.Start();
            if (ServiceProvider.Settings.StartMinimized)
                this.WindowState = WindowState.Minimized;
            SortCameras();
        }

        void DeviceManager_CameraDisconnected(ICameraDevice cameraDevice)
        {
            MyNotifyIcon.HideBalloonTip();
            MyNotifyIcon.ShowBalloonTip("Camera disconnected", cameraDevice.LoadProperties().DeviceName, BalloonIcon.Info);
        }

        void DeviceManager_CameraConnected(ICameraDevice cameraDevice)
        {
            MyNotifyIcon.HideBalloonTip();
            MyNotifyIcon.ShowBalloonTip("Camera connected", cameraDevice.LoadProperties().DeviceName, BalloonIcon.Info);
            SortCameras();
        }

        private void CheckForUpdate()
        {
            if ((DateTime.Now - ServiceProvider.Settings.LastUpdateCheckDate).TotalDays > 7)
            {
                if (!ServiceProvider.Branding.CheckForUpdate)
                    return;

                Thread.Sleep(2000);
                ServiceProvider.Settings.LastUpdateCheckDate = DateTime.Now;
                ServiceProvider.Settings.Save();
                Dispatcher.Invoke(new Action(() => NewVersionWnd.CheckForUpdate(false)));
            }
            else
            {
                if (!ServiceProvider.Branding.ShowStartupScreen)
                    return;

                // show welcome screen only if not start minimized
                if (!ServiceProvider.Settings.StartMinimized)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        var wnd = new Welcome();
                        wnd.ShowDialog();
                    }));
                }
            }
        }

        private void DeviceManager_CameraSelected(ICameraDevice oldcameraDevice, ICameraDevice newcameraDevice)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    delegate
                        {
                            //btn_capture_noaf.IsEnabled = newcameraDevice != null && newcameraDevice.GetCapability(CapabilityEnum.CaptureNoAf);
                            ((Flyout) Flyouts.Items[0]).IsOpen = false;
                            Title = (ServiceProvider.Branding.ApplicationTitle ?? "digiCamControl") + " - " +
                                    (newcameraDevice == null ? "" : newcameraDevice.DisplayName);
                        }));
        }

        private void ExecuteExportPlugin(IExportPlugin obj)
        {
            obj.Execute();
        }

        private void ExecuteToolPlugin(IToolPlugin obj)
        {
            obj.Execute();
        }

        private void HideFlatOuts()
        {
            ((Flyout) Flyouts.Items[0]).IsOpen = false;
        }

        private void DeviceManager_PhotoCaptured(object sender, PhotoCapturedEventArgs eventArgs)
        {
            if (ServiceProvider.Settings.UseParallelTransfer)
            {
                PhotoCaptured(eventArgs);
            }
            else
            {
                lock (_locker)
                {
                    PhotoCaptured(eventArgs);
                }
            }
        }

        /// <summary>
        /// Photoes the captured.
        /// </summary>
        /// <param name="o">The o.</param>
        private void PhotoCaptured(object o)
        {
            PhotoCapturedEventArgs eventArgs = o as PhotoCapturedEventArgs;
            if (eventArgs == null)
                return;
            try
            {
                Log.Debug("Photo transfer begin.");
                eventArgs.CameraDevice.IsBusy = true;
                CameraProperty property = eventArgs.CameraDevice.LoadProperties();
                PhotoSession session = (PhotoSession) eventArgs.CameraDevice.AttachedPhotoSession ??
                                       ServiceProvider.Settings.DefaultSession;
                StaticHelper.Instance.SystemMessage = "";
                if (!eventArgs.CameraDevice.CaptureInSdRam)
                {
                    if (property.NoDownload)
                    {
                        eventArgs.CameraDevice.IsBusy = false;
                        return;
                    }
                    var extension = Path.GetExtension(eventArgs.FileName);
                    if (extension != null && (session.DownloadOnlyJpg && extension.ToLower() != ".jpg"))
                    {
                        eventArgs.CameraDevice.IsBusy = false;
                        return;
                    }
                }
                StaticHelper.Instance.SystemMessage = TranslationStrings.MsgPhotoTransferBegin;
                string fileName = "";
                if (!session.UseOriginalFilename || eventArgs.CameraDevice.CaptureInSdRam)
                {
                    fileName =
                        session.GetNextFileName(eventArgs.FileName, eventArgs.CameraDevice);
                }
                else
                {
                    fileName = Path.Combine(session.Folder, eventArgs.FileName);
                    if (File.Exists(fileName) && !session.AllowOverWrite)
                        fileName =
                            StaticHelper.GetUniqueFilename(
                                Path.GetDirectoryName(fileName) + "\\" + Path.GetFileNameWithoutExtension(fileName) +
                                "_", 0,
                                Path.GetExtension(fileName));
                }

                if (session.AllowOverWrite && File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                // make lower case extension 
                if (session.LowerCaseExtension && !string.IsNullOrEmpty(Path.GetExtension(fileName)))
                {
                    fileName = Path.Combine(Path.GetDirectoryName(fileName),
                        Path.GetFileNameWithoutExtension(fileName) + Path.GetExtension(fileName).ToLower());
                }

                if (!Directory.Exists(Path.GetDirectoryName(fileName)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                }

                Log.Debug("Transfer started :" + fileName);

                string tempFile = Path.GetTempFileName();

                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                eventArgs.CameraDevice.TransferFile(eventArgs.Handle, tempFile);

                File.Copy(tempFile, fileName);

                string backupfile = null;
                if (session.BackUp)
                    backupfile = session.CopyBackUp(tempFile, fileName);

                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                if (session.WriteComment)
                {
                    if (!string.IsNullOrEmpty(session.Comment))
                        Exiv2Helper.SaveComment(fileName, session.Comment);
                    if (session.SelectedTag1 != null && !string.IsNullOrEmpty(session.SelectedTag1.Value))
                        Exiv2Helper.AddKeyword(fileName, session.SelectedTag1.Value);
                    if (session.SelectedTag2 != null && !string.IsNullOrEmpty(session.SelectedTag2.Value))
                        Exiv2Helper.AddKeyword(fileName, session.SelectedTag2.Value);
                    if (session.SelectedTag3 != null && !string.IsNullOrEmpty(session.SelectedTag3.Value))
                        Exiv2Helper.AddKeyword(fileName, session.SelectedTag3.Value);
                    if (session.SelectedTag4 != null &&  !string.IsNullOrEmpty(session.SelectedTag4.Value))
                        Exiv2Helper.AddKeyword(fileName, session.SelectedTag4.Value);
                }

                if (session.ExternalData != null)
                    session.ExternalData.FileName = fileName;

                // prevent crash og GUI when item count updated
                Dispatcher.Invoke(new Action(delegate
                {
                    _selectedItem = session.AddFile(fileName);
                    _selectedItem.BackupFileName = backupfile;
                    _selectedItem.Series = session.Series;
                    _selectedItem.AddTemplates(eventArgs.CameraDevice,session);
                }));

                foreach (AutoExportPluginConfig plugin in ServiceProvider.Settings.DefaultSession.AutoExportPluginConfigs)
                {
                    if(!plugin.IsEnabled)
                        continue;
                    var pl = ServiceProvider.PluginManager.GetAutoExportPlugin(plugin.Type);
                    try
                    {
                        pl.Execute(_selectedItem, plugin);
                        ServiceProvider.Analytics.PluginExecute(plugin.Type);
                    }
                    catch (Exception ex)
                    {
                        plugin.IsError = true;
                        plugin.Error = ex.Message;
                        plugin.IsRedy = true;
                        Log.Error("Error to apply plugin", ex);
                    }
                }


                if (ServiceProvider.Settings.MinimizeToTrayIcon && !IsVisible)
                {
                    MyNotifyIcon.HideBalloonTip();
                    MyNotifyIcon.ShowBalloonTip("Photo transfered", fileName, BalloonIcon.Info);
                }

                //select the new file only when the multiple camera support isn't used to prevent high CPU usage on raw files
                if (ServiceProvider.Settings.AutoPreview &&
                    !ServiceProvider.WindowsManager.Get(typeof (MultipleCameraWnd)).IsVisible &&
                    !ServiceProvider.Settings.UseExternalViewer)
                {
                    if ((Path.GetExtension(fileName).ToLower() == ".jpg" && ServiceProvider.Settings.AutoPreviewJpgOnly) ||
                        !ServiceProvider.Settings.AutoPreviewJpgOnly)
                    {
                        if (ServiceProvider.Settings.DelayImageLoading &&
                            (DateTime.Now - _lastLoadTime).TotalSeconds < 4)
                        {
                            _selectiontimer.Stop();
                            _selectiontimer.Start();
                        }
                        else
                        {
                            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.Select_Image, _selectedItem);
                        }
                    }
                }
                _lastLoadTime = DateTime.Now;
                //ServiceProvider.Settings.Save(session);
                StaticHelper.Instance.SystemMessage = TranslationStrings.MsgPhotoTransferDone;
                eventArgs.CameraDevice.IsBusy = false;
                //show fullscreen only when the multiple camera support isn't used
                if (ServiceProvider.Settings.Preview &&
                    !ServiceProvider.WindowsManager.Get(typeof (MultipleCameraWnd)).IsVisible &&
                    !ServiceProvider.Settings.UseExternalViewer)
                    ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.FullScreenWnd_ShowTimed);
                if (ServiceProvider.Settings.UseExternalViewer &&
                    File.Exists(ServiceProvider.Settings.ExternalViewerPath))
                {
                    string arg = ServiceProvider.Settings.ExternalViewerArgs;
                    arg = arg.Contains("%1") ? arg.Replace("%1", fileName) : arg + " " + fileName;
                    PhotoUtils.Run(ServiceProvider.Settings.ExternalViewerPath, arg, ProcessWindowStyle.Normal);
                }
                if (ServiceProvider.Settings.PlaySound)
                {
                    PhotoUtils.PlayCaptureSound();
                }
            }
            catch (Exception ex)
            {
                eventArgs.CameraDevice.IsBusy = false;
                StaticHelper.Instance.SystemMessage = string.Format(TranslationStrings.MsgPhotoTransferError, ex.Message);
                Log.Error("Transfer error !", ex);
            }
            Log.Debug("Photo transfer done.");
            // not indicated to be used 
            GC.Collect();
            //GC.WaitForPendingFinalizers();
        }

        public RelayCommand<CameraPreset> SelectPresetCommand { get; private set; }
        public RelayCommand<CameraPreset> DeletePresetCommand { get; private set; }
        public RelayCommand<CameraPreset> LoadInAllPresetCommand { get; private set; }
        public RelayCommand<CameraPreset> VerifyPresetCommand { get; private set; }

        public RelayCommand<IExportPlugin> ExecuteExportPluginCommand { get; private set; }

        public RelayCommand<IToolPlugin> ExecuteToolPluginCommand { get; private set; }

        private void SelectPreset(CameraPreset preset)
        {
            if (preset == null)
                return;
            try
            {
                preset.Set(ServiceProvider.DeviceManager.SelectedCameraDevice);
            }
            catch (Exception exception)
            {
                Log.Error("Error set preset", exception);
            }
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            if (ServiceProvider.DeviceManager.SelectedCameraDevice == null)
                return;
            Log.Debug("Main window capture started");
            try
            {
                if (ServiceProvider.DeviceManager.SelectedCameraDevice.ShutterSpeed != null &&
                    ServiceProvider.DeviceManager.SelectedCameraDevice.ShutterSpeed.Value == "Bulb")
                {
                    if (ServiceProvider.DeviceManager.SelectedCameraDevice.GetCapability(CapabilityEnum.Bulb))
                    {
                        ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.BulbWnd_Show,
                                                                      ServiceProvider.DeviceManager.SelectedCameraDevice);
                        return;
                    }
                    else
                    {
                        StaticHelper.Instance.SystemMessage = TranslationStrings.MsgBulbModeNotSupported;
                        return;
                    }
                }
                CameraHelper.Capture(ServiceProvider.DeviceManager.SelectedCameraDevice);
            }
            catch (DeviceException exception)
            {
                StaticHelper.Instance.SystemMessage = exception.Message;
                Log.Error("Take photo", exception);
            }
            catch (Exception exception)
            {
                StaticHelper.Instance.SystemMessage = exception.Message;
                Log.Error("Take photo", exception);
            }
        }

        private void btn_edit_Sesion_Click(object sender, RoutedEventArgs e)
        {
            //if (File.Exists(ServiceProvider.Settings.DefaultSession.ConfigFile))
            //{
            //    File.Delete(ServiceProvider.Settings.DefaultSession.ConfigFile);
            //}
            EditSession editSession = new EditSession(ServiceProvider.Settings.DefaultSession);
            editSession.Owner = this;
            ServiceProvider.Settings.ApplyTheme(editSession);
            editSession.ShowDialog();
            ServiceProvider.Settings.Save(ServiceProvider.Settings.DefaultSession);
        }

        private void btn_add_Sesion_Click(object sender, RoutedEventArgs e)
        {
            var defaultsessionfile = Path.Combine(Settings.SessionFolder, "Default.xml");
            var session = new PhotoSession();
            // copy session with default name
            if (File.Exists(defaultsessionfile))
            {
                session = ServiceProvider.Settings.LoadSession(defaultsessionfile);
                session.Files.Clear();
            }
            var editSession = new EditSession(session);
            editSession.Owner = this;
            ServiceProvider.Settings.ApplyTheme(editSession);
            if (editSession.ShowDialog() == true)
            {
                ServiceProvider.Settings.Add(editSession.Session);
                ServiceProvider.Settings.DefaultSession = editSession.Session;
            }
        }


        private void Window_Closed(object sender, EventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(CmdConsts.All_Close);
        }

        private void but_timelapse_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.TimeLapseWnd_Show);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
        }

        private void but_fullscreen_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.FullScreenWnd_Show);
        }

        private void btn_about_Click(object sender, RoutedEventArgs e)
        {
            AboutWnd wnd = new AboutWnd();
            wnd.ShowDialog();
        }


        private void btn_br_Click(object sender, RoutedEventArgs e)
        {
            BraketingWnd wnd = new BraketingWnd(ServiceProvider.DeviceManager.SelectedCameraDevice,
                                                ServiceProvider.Settings.DefaultSession);
            wnd.ShowDialog();
        }


        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            PhotoUtils.Run("http://www.digicamcontrol.com/", "");
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e)
        {
            NewVersionWnd.CheckForUpdate(true);
        }


        private void btn_browse_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.BrowseWnd_Show);
        }

        private void SetLayout(string enumname)
        {
            LayoutTypeEnum type;
            if (Enum.TryParse(enumname, true, out type))
            {
            }
            SetLayout(type);
        }

        private void SetLayout(LayoutTypeEnum type)
        {
            ServiceProvider.Settings.SelectedLayout = type.ToString();
            if (StackLayout.Children.Count > 0)
            {
                var cnt = StackLayout.Children[0] as LayoutBase;
                if (cnt != null)
                    cnt.UnInit();
            }
            switch (type)
            {
                case LayoutTypeEnum.Normal:
                    {
                        StackLayout.Children.Clear();
                        LayoutNormal control = new LayoutNormal();
                        StackLayout.Children.Add(control);
                    }
                    break;
                case LayoutTypeEnum.Grid:
                    {
                        StackLayout.Children.Clear();
                        LayoutGrid control = new LayoutGrid();
                        StackLayout.Children.Add(control);
                    }
                    break;
                case LayoutTypeEnum.GridRight:
                    {
                        StackLayout.Children.Clear();
                        LayoutGridRight control = new LayoutGridRight();
                        StackLayout.Children.Add(control);
                    }
                    break;
            }
        }

        private void MenuItem_Click_6(object sender, RoutedEventArgs e)
        {
            SetLayout(LayoutTypeEnum.Normal);
        }

        private void MenuItem_Click_7(object sender, RoutedEventArgs e)
        {
            SetLayout(LayoutTypeEnum.GridRight);
        }

        private void MenuItem_Click_8(object sender, RoutedEventArgs e)
        {
            SetLayout(LayoutTypeEnum.Grid);
        }

        private void btn_Tags_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(
                ServiceProvider.WindowsManager.Get(typeof (TagSelectorWnd)).IsVisible
                    ? WindowsCmdConsts.TagSelectorWnd_Hide
                    : WindowsCmdConsts.TagSelectorWnd_Show);
        }

        private void btn_del_Sesion_Click(object sender, RoutedEventArgs e)
        {
            if (ServiceProvider.Settings.PhotoSessions.Count > 1)
            {
                try
                {
                    if (
                        MessageBox.Show(
                            string.Format(TranslationStrings.MsgDeleteSessionQuestion,
                                          ServiceProvider.Settings.DefaultSession.Name),
                            TranslationStrings.LabelDeleteSession, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        PhotoSession session = ServiceProvider.Settings.DefaultSession;
                        if (!string.IsNullOrEmpty(session.ConfigFile) && File.Exists(session.ConfigFile))
                            File.Delete(session.ConfigFile);
                        ServiceProvider.Settings.PhotoSessions.Remove(session);
                        ServiceProvider.Settings.DefaultSession = ServiceProvider.Settings.PhotoSessions[0];
                        ServiceProvider.Settings.Save();
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("Unable to remove session", exception);
                    MessageBox.Show(TranslationStrings.LabelUnabletoDeleteSession + exception.Message);
                }
            }
            else
            {
                MessageBox.Show(TranslationStrings.MsgLastSessionCantBeDeleted);
            }
        }

        private void btn_settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWnd wnd = new SettingsWnd();
            wnd.ShowDialog();
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run",
                    true);

                if (rk == null) return;

                if (ServiceProvider.Settings.StartupWithWindows)
                {
                    rk.SetValue(Settings.AppName, System.Reflection.Assembly.GetExecutingAssembly().Location);
                }
                else
                    rk.DeleteValue(Settings.AppName, false);
            }
            catch (Exception ex)
            {
                this.ShowMessageAsync("Usable to set startup", ex.Message);
                Log.Error("Usable to set startup", ex);
            }
        }

        private void btn_menu_Click(object sender, RoutedEventArgs e)
        {
            ((Flyout) Flyouts.Items[0]).IsOpen = !((Flyout) Flyouts.Items[0]).IsOpen;
        }

        private void mnu_forum_Click(object sender, RoutedEventArgs e)
        {
            PhotoUtils.Run("http://www.digicamcontrol.com/forum/", "");
        }

        private void btn_donate_Click(object sender, RoutedEventArgs e)
        {
            PhotoUtils.Donate();
        }

        private void btn_help_Click(object sender, RoutedEventArgs e)
        {
            HelpProvider.Run(HelpSections.MainMenu);
        }

        private void but_download_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.DownloadPhotosWnd_Show,
                                                          ServiceProvider.DeviceManager.SelectedCameraDevice);
        }

        private void MetroWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            TriggerClass.KeyDown(e);
            if (e.Key == Key.Escape)
            {
                HideFlatOuts();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.MultipleCameraWnd_Show);
            HideFlatOuts();
        }

        private void btn_sort_Click(object sender, RoutedEventArgs e)
        {
            SortCameras(true);
        }

        private void btn_sort_desc_Click(object sender, RoutedEventArgs e)
        {
            SortCameras(false);
        }

        private void SortCameras()
        {
            SortCameras(_sortCameraOreder);
        }

        private void SortCameras(bool asc)
        {
            _sortCameraOreder = asc;

            // making sure the camera names are refreshed from properties
            foreach (var device in ServiceProvider.DeviceManager.ConnectedDevices)
            {
                device.LoadProperties();
            }
            if (asc)
            {
                ServiceProvider.DeviceManager.ConnectedDevices =
                    new AsyncObservableCollection<ICameraDevice>(
                        ServiceProvider.DeviceManager.ConnectedDevices.OrderBy(x => x.LoadProperties().SortOrder).ThenBy(x=>x.DisplayName));
            }
            else
            {
                ServiceProvider.DeviceManager.ConnectedDevices =
                    new AsyncObservableCollection<ICameraDevice>(
                        ServiceProvider.DeviceManager.ConnectedDevices.OrderByDescending(x => x.LoadProperties().SortOrder).ThenByDescending(x => x.DisplayName));
            }
        }

        private void but_star_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.BulbWnd_Show,
                                                          ServiceProvider.DeviceManager.SelectedCameraDevice);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.Del_Image);
                e.Handled = true;
            }
        }

        private void mnu_send_log_Click(object sender, RoutedEventArgs e)
        {
            var wnd = new ErrorReportWnd("Log file");
            wnd.ShowDialog();
        }

        private void btn_changelog_Click(object sender, RoutedEventArgs e)
        {
            NewVersionWnd.ShowChangeLog();
        }

        #region Implementation of INotifyPropertyChanged

        public virtual event PropertyChangedEventHandler PropertyChanged;

        public virtual void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        #endregion

        private void but_wifi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(ServiceProvider.Settings.WifiIp))
                    ServiceProvider.Settings.WifiIp = "192.168.1.1";
                var wnd = new GetIpWnd();
                wnd.Owner = this;
                if (wnd.ShowDialog() == true)
                    ServiceProvider.DeviceManager.ConnectToServer(ServiceProvider.Settings.WifiIp, ServiceProvider.Settings.SelectedWifi);
            }
            catch (Exception exception)
            {
                Log.Error("Unable to connect to WiFi device", exception);
                this.ShowMessageAsync("Error", "Unable to connect to WiFi device " + exception.Message);
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            CameraPreset cameraPreset = new CameraPreset();
            SavePresetWnd wnd = new SavePresetWnd(cameraPreset);
            wnd.Owner = this;
            if (wnd.ShowDialog() == true)
            {
                foreach (CameraPreset preset in ServiceProvider.Settings.CameraPresets)
                {
                    if (preset.Name == cameraPreset.Name)
                    {
                        cameraPreset = preset;
                        break;
                    }
                }
                cameraPreset.Get(ServiceProvider.DeviceManager.SelectedCameraDevice);
                if (!ServiceProvider.Settings.CameraPresets.Contains(cameraPreset))
                    ServiceProvider.Settings.CameraPresets.Add(cameraPreset);
                ServiceProvider.Settings.Save();
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            PresetEditWnd wnd = new PresetEditWnd();
            wnd.Owner = this;
            wnd.ShowDialog();
        }

        private void btn_refresh_Sesion_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.Settings.LoadData(ServiceProvider.Settings.DefaultSession);
        }

        private void btn_open_folder_Sesion_Click(object sender, RoutedEventArgs e)
        {
            PhotoUtils.Run(ServiceProvider.Settings.DefaultSession.Folder);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && ServiceProvider.Settings.MinimizeToTrayIcon)
            {
                this.Hide();
                MyNotifyIcon.HideBalloonTip();
                MyNotifyIcon.ShowBalloonTip("digiCamControl", "Application was minimized \n Double click to restore", BalloonIcon.Info);
            }
        }

        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
        }

        private void but_refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ServiceProvider.DeviceManager.ConnectToCamera();
            }
            catch (Exception exception)
            {
                Log.Error("Error to connect", exception);
                this.ShowMessageAsync("Error", "Unable to connect " + exception.Message);
            }
        }

        private void but_print_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.PrintWnd_Show);
        }

        private void but_qr_Click(object sender, RoutedEventArgs e)
        {
            QrCodeWnd wnd = new QrCodeWnd();
            wnd.Owner = this;
            wnd.Show();
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if(e.Delta>0)
                ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.Next_Image);
            else
                ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.Prev_Image);
        }

        private void btn_barcode_Click(object sender, RoutedEventArgs e)
        {
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.BarcodeWnd_Show);
        }


    }
}
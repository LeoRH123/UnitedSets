using EasyCSharp;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using System;
using WindowEx = WinWrapper.Windowing.Window;
using Keyboard = WinWrapper.Input.Keyboard;
using UnitedSets.Classes;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using System.ComponentModel;
using System.Diagnostics;
using WinUIEx.Messaging;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using UnitedSets.Tabs;
using CommunityToolkit.WinUI;
using Microsoft.UI.Input;
using UnitedSets.UI.Popups;
using UnitedSets.UI.FlyoutModules;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using WindowHoster;
using UnitedSets.Windows;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using CommunityToolkit.Mvvm.Input;
using WinWrapper.Windowing;

namespace UnitedSets.UI.AppWindows;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : INotifyPropertyChanged
{
    AddTabPopup? AddTabPopup;
    private async partial void OnAddTabButtonClick()
    {
        if (Keyboard.IsShiftDown)
        {
            AddSplitableTab();
        }
        else
        {
            AddTabPopup ??= new();
            Win32Window.Minimize();
            await AddTabPopup.ShowAsync();
            Win32Window.Restore();
            var result = AddTabPopup.Result;
            if (WindowHostTab.Create(result) is { } tab)
            {
                UnitedSetsApp.Current.Tabs.Add(tab);
                UnitedSetsApp.Current.SelectedTab = tab;
            }
        }
    }
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    void AddSplitableTab()
    {
        var newTab = new CellTab(Constants.IsAltTabVisible);
        UnitedSetsApp.Current.Tabs.Add(newTab);
        UnitedSetsApp.Current.SelectedTab = newTab;
    }
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task ExportData()
    {
        var res = await ExportImportInputPage.ShowExportImport(true, this);
        if (res == null)
            return;
        UnitedSetsApp.Current.Configuration.PersistantService.ExportSettings(res.FullFilename, res.OnlyExportNonDefault);
    }
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task ImportData()
    {
        var res = await ExportImportInputPage.ShowExportImport(false, this);
        if (res == null)
            return;
        UnitedSetsApp.Current.Configuration.PersistantService.ImportSettings(res.FullFilename);
    }

    private partial void TabDragStarting(TabViewTabDragStartingEventArgs args)
    {
        if (args.Item is WindowHostTab item)
            args.Data.Properties.Add(Constants.UnitedSetsTabWindowDragProperty, (long)item.Window.Handle);
    }

    private partial void OnDragItemOverTabView(DragEventArgs e)
    {
        if (e.DataView.Properties?.ContainsKey(Constants.UnitedSetsTabWindowDragProperty) == true)
            e.AcceptedOperation = DataPackageOperation.Move;
    }

    public partial void OnDragOverTabViewItem(object sender)
    {
        if (sender is FrameworkElement tvi && tvi.Tag is TabBase tb)
            TabView.SelectedIndex = UnitedSetsApp.Current.Tabs.IndexOf(tb);
    }

    private partial void OnDropOverTabView(DragEventArgs e)
    {
        if (e.DataView.Properties.TryGetValue(Constants.UnitedSetsTabWindowDragProperty, out var _a) && _a is long a)
        {

            var window = WindowEx.FromWindowHandle((nint)a);
            var ret = window.Owner.SendMessage(
                Constants.UnitedSetCommunicationChangeWindowOwnership, new(), window);
            var pt = e.GetPosition(TabView);
            var finalIdx = (
                from index in Enumerable.Range(0, UnitedSetsApp.Current.Tabs.Count)
                let ele = TabView.ContainerFromIndex(index) as UIElement
                let posele = ele.TransformToVisual(TabView).TransformPoint(default)
                let size = ele.ActualSize
                let IsMoreThanTopLeft = pt.X >= posele.X && pt.Y >= posele.Y
                let IsLessThanBotRigh = pt.X <= posele.X + size.X && pt.Y <= posele.Y + size.Y
                where IsMoreThanTopLeft && IsLessThanBotRigh
                select index
            ).FirstOrDefault();
            if (WindowHostTab.Create(window) is { } tab)
                UnitedSetsApp.Current.Tabs.Insert(finalIdx, tab);
        }
    }

    private partial void TabDroppedOutside(TabViewTabDroppedOutsideEventArgs args)
    {
        if (args.Tab.Tag is TabBase Tab)
            Tab.DetachAndDispose(JumpToCursor: true);
    }

    private partial void TabSelectionChanged()
    {
        UnitedSetsHomeBackground.Visibility =
                TabView.SelectedIndex != -1 && UnitedSetsApp.Current.Tabs[TabView.SelectedIndex] is CellTab ?
                Visibility.Collapsed :
                Visibility.Visible;
        UpdateTitle();
    }
    private async Task SafeClose(TabBase tab)
    {
        try
        {
            var tsk = tab.TryCloseAsync();
            await await Task.WhenAny(tsk, Task.Delay(TimeSpan.FromSeconds(3)));//yes double await to get exceptions....
            if (tsk.IsCompletedSuccessfully)
                return;
        }
        catch (Exception)
        {
        }
        tab.DetachAndDispose();
    }

    private partial void OnWindowClosing(AppWindowClosingEventArgs e)
    {
        e.Cancel = true;//as we will just exit if we want to actually close
        if (UnitedSetsApp.Current.Tabs.Count > 1)
        {
            Win32Window.Focus();
            ClosingFlyout.XamlRoot = Content.XamlRoot;
            ClosingFlyout.ShowAt((FrameworkElement)Content);
        }else
            RequestCloseAsync(CloseMode.ReleaseWindow);
    }
    public enum CloseMode
    {
        ReleaseWindow,
        CloseWindow,
        SaveCloseWindow
    }
    // for XAML Bindings
    readonly CloseMode ReleaseWindowCloseMode = CloseMode.ReleaseWindow;
    readonly CloseMode CloseWindowCloseMode = CloseMode.CloseWindow;
    readonly CloseMode SaveCloseWindowCloseMode = CloseMode.SaveCloseWindow;
    [RelayCommand]
    [DoesNotReturn]
    public async Task RequestCloseAsync(CloseMode closeMode)
    {
        ClosingFlyout.Hide();
        switch (closeMode)
        {
            case CloseMode.ReleaseWindow:
                // Release all windows
                while (UnitedSetsApp.Current.Tabs.Count > 0)
                {
                    var Tab = UnitedSetsApp.Current.Tabs.First();
                    UnitedSetsApp.Current.Tabs.Remove(Tab);
                    Tab.DetachAndDispose(JumpToCursor: false);
                }
                await TimerStop();

                await UnitedSetsApp.Current.Suicide();

                return;
            case CloseMode.CloseWindow:
                // Close all windows
                await Task.Delay(100);
                await Task.WhenAll(UnitedSetsApp.Current.Tabs.ToArray().Select(SafeClose));
                await UnitedSetsApp.Current.Suicide();
                return;
            case CloseMode.SaveCloseWindow:
                UnitedSetsApp.Current.Configuration.SaveCurrentSession();
                goto case CloseMode.CloseWindow;
            default:
                throw new ArgumentOutOfRangeException(nameof(closeMode));
        }
    }
    private partial void CellWindowDropped(Cell cell, nint HwndId)
    {
        if (cell == null)
            throw new Exception("Only cells should be generating this event");
        var window = WindowEx.FromWindowHandle(HwndId);
        var ret = window.Owner.SendMessage(
            Constants.UnitedSetCommunicationChangeWindowOwnership,
            default,
            window
        );
        var tab =
            UnitedSetsApp.Current.Tabs.ToArray().OfType<CellTab>()
            .First(tab => tab._MainCell.AllSubCells.Any(c => c == cell));

        var registeredWindow = RegisteredWindow.Register(window);
        if (registeredWindow is null) throw new Exception();
        cell.RegisterWindow(registeredWindow);
    }
    private partial void OnWindowMessageReceived(WindowMessageEventArgs e)
    {
        var id = (WindowMessages)e.Message.MessageId;
        if (id == Constants.UnitedSetCommunicationChangeWindowOwnership)
        {
            var winPtr = e.Message.LParam;
            if (UnitedSetsApp.Current.Tabs.ToArray().FirstOrDefault(x => x.Windows.Any(y => y == winPtr)) is TabBase Tab)
            {
                Tab.DetachAndDispose(false);
                e.Result = 1;
            }
            else e.Result = 0;
            e.Handled = true;
        }
        e.Handled = false;
    }
}

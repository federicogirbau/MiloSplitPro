using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiloSplitPro.App.ViewModels;

namespace MiloSplitPro.App;

public partial class MainWindow : Window
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var file = files[0];
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".wav" or ".mp3" or ".flac" or ".m4a" or ".ogg")
                {
                    ViewModel?.LoadInputAudioFile(file);
                }
                else
                {
                    MessageBox.Show("Formato no soportado. Por favor arrastrá un archivo WAV, MP3, FLAC, M4A u OGG.", "Milo Split Pro", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Ignore spacebar if focused on an input element
        if (e.Key == Key.Space)
        {
            var focused = Keyboard.FocusedElement;
            if (focused is TextBox or PasswordBox)
            {
                return;
            }

            if (ViewModel != null && ViewModel.HasSeparatedTracks)
            {
                ViewModel.PlayPause();
                e.Handled = true;
            }
        }
    }

    private bool _isUserDraggingTimeline;

    private void TimelineSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isUserDraggingTimeline = true;
        HandleTimelineSeek(sender, e);
    }

    private void TimelineSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isUserDraggingTimeline && e.LeftButton == MouseButtonState.Pressed)
        {
            HandleTimelineSeek(sender, e);
        }
    }

    private void TimelineSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isUserDraggingTimeline)
        {
            _isUserDraggingTimeline = false;
            HandleTimelineSeek(sender, e);
        }
    }

    private void HandleTimelineSeek(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider && slider.ActualWidth > 0)
        {
            var point = e.GetPosition(slider);
            var ratio = Math.Clamp(point.X / slider.ActualWidth, 0.0, 1.0);
            var targetSec = ratio * slider.Maximum;
            ViewModel?.SeekTimeline(targetSec);
        }
    }

    private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void TitleBar_Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ViewModel?.CancelSeparation();
    }
}
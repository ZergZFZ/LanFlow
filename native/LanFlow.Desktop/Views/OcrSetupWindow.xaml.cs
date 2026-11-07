using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LanFlow.Desktop.Services.Ocr;
using Microsoft.Win32;

namespace LanFlow.Desktop.Views;

/// <summary>
/// 首次启用向导：同意并下载 / 手动导入。成功关闭后 <see cref="Enabled"/> 为 true。
/// </summary>
public partial class OcrSetupWindow : Window
{
    private readonly OcrBundleService _service = new();
    private CancellationTokenSource? _cts;
    private bool _busy;

    public OcrSetupWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _cts?.Cancel();
    }

    public bool Enabled { get; private set; }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        StatusText.Text = "正在查询发布信息…";

        try
        {
            var progress = new Progress<double>(value =>
            {
                Progress.Value = value;
                StatusText.Text = $"正在下载组件包… {value * 100:F0}%";
            });

            var zip = await _service.DownloadLatestAsync(progress, _cts.Token);
            try
            {
                StatusText.Text = "正在校验并安装…";
                await _service.InstallFromZipAsync(zip, _cts.Token);
            }
            finally
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch { /* ignore */ }
            }

            FinishInstall();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "下载已取消。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "安装失败：" + ex.Message + "（可尝试“从文件导入”）";
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new OpenFileDialog
        {
            Title = "选择 OCR 组件包",
            Filter = "OCR 组件包 (*.zip)|*.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        StatusText.Text = "正在校验并安装…";

        try
        {
            await _service.InstallFromZipAsync(dialog.FileName, _cts.Token);
            FinishInstall();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "安装已取消。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "导入失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private void FinishInstall()
    {
        Enabled = true;
        OcrEngineHost.Instance.Reset();
        StatusText.Text = "组件已安装完成。";
        try
        {
            DialogResult = true;
        }
        catch
        {
            Close();
        }
    }

    private void SetBusy(bool busy)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy) Progress.Value = 0;
        // 忙碌时只禁用动作按钮，保留"取消"可用（_cts 链路已支持取消下载，P3-6）。
        DownloadButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        CancelButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}

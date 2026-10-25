using Assets.Editor.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xaml.Effects.Toolkit.Model;

namespace Assets.Editor.Models;

public abstract class ResourceOperationModel : DialogModel
{
    private CancellationTokenSource cancellation;
    private bool busy, closeWhenStopped;
    private double progress;
    private string statusText = "请选择源文件和输出位置。", countsText = "", cancelText = "关闭";
    public bool IsBusy => busy;
    public bool CanEdit => !busy;
    public double Progress { get => progress; protected set => SetProperty(ref progress, value); }
    public string StatusText { get => statusText; protected set => SetProperty(ref statusText, value); }
    public string CountsText { get => countsText; private set => SetProperty(ref countsText, value); }
    public string CancelText { get => cancelText; private set => SetProperty(ref cancelText, value); }
    protected bool HasChanges { get; set; }
    protected virtual string CancellationMessage => "已取消，未生成输出。";

    private protected async Task RunOperationAsync(Action<IProgress<ResourceOperationProgress>, CancellationToken> work,
        string completion, bool closeOnSuccess = false)
    {
        if (IsBusy) return;
        var activeCancellation = new CancellationTokenSource();
        cancellation = activeCancellation;
        SetBusy(true);
        Progress = 0;
        CountsText = "";
        StatusText = "正在打开资源…";
        var updates = new Progress<ResourceOperationProgress>(value =>
        {
            if (!IsBusy || !ReferenceEquals(cancellation, activeCancellation) || activeCancellation.IsCancellationRequested) return;
            Progress = Math.Max(Progress, value.Total == 0 ? 0 : Math.Min(99, value.Completed * 100.0 / value.Total));
            CountsText = $"{value.Completed:N0} / {value.Total:N0}";
            StatusText = value.Status;
        });
        bool succeeded = false;
        try
        {
            await Task.Run(() => work(updates, activeCancellation.Token));
            Progress = 100;
            StatusText = completion;
            succeeded = true;
        }
        catch (OperationCanceledException) { StatusText = CancellationMessage; }
        catch (Exception ex) { StatusText = $"操作失败：{ex.Message}" + (HasChanges ? " 已写入的资源会保留。" : ""); }
        finally
        {
            SetBusy(false);
            cancellation.Dispose();
            cancellation = null;
        }
        if (closeWhenStopped || (succeeded && closeOnSuccess)) DialogResult = HasChanges || succeeded;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEdit));
        CancelText = value ? "取消任务" : "关闭";
        SubmitCommand.NotifyCanExecuteChanged();
    }

    public bool RequestWindowClose()
    {
        if (!IsBusy) return true;
        closeWhenStopped = true;
        RequestCancellation();
        return false;
    }

    private void RequestCancellation()
    {
        cancellation?.Cancel();
        StatusText = "正在取消，请等待当前帧或写入批次结束…";
    }

    protected override void Execute_Cancel()
    {
        if (IsBusy) RequestCancellation();
        else DialogResult = HasChanges;
    }

    protected override void Execute_Exit()
    {
        if (RequestWindowClose()) DialogResult = HasChanges;
    }
}

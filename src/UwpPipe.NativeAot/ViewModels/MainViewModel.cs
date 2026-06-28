using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using UwpPipe.Common;
using UwpPipe.Common.Pipes;
using UwpPipe.Common.Messages;

namespace UwpPipe.NativeAot.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private PipeBase? pipe;
    private CancellationTokenSource pipeCts = new();

    public PipeMode[] PipeModes { get; } = [PipeMode.Client, PipeMode.Server];
    public string StartOrStopPipeButtonString { get => IsPipeCreated ? "停止" : "启动"; }

    [ObservableProperty]
    public partial PipeMode SelectedPipeMode { get; set; }

    [ObservableProperty]
    [MemberNotNullWhen(true, nameof(pipe))]
    public partial bool IsPipeStarted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartOrStopPipeButtonString))]
    [MemberNotNullWhen(true, nameof(pipe))]
    public partial bool IsPipeCreated { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<Message> Messages { get; set; } = [];

    [ObservableProperty]
    public partial string? ToBeSentMessage { get; set; }

    [ObservableProperty]
    public partial bool InfoBarVisible { get; set; }

    [ObservableProperty]
    public partial string InfoBarTitle { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity InfoBarSeverity { get; set; }

    [ObservableProperty]
    public partial bool InfoBarClosable { get; set; }

    [RelayCommand]
    private async Task StartOrStopPipe()
    {
        pipeCts.Cancel();

        if (IsPipeCreated)
        {
            await ClosePipeAsync();
            HideInfoBar();
        }
        else
        {
            pipeCts = new();

            switch (SelectedPipeMode)
            {
                case PipeMode.Server:
                    pipe = new ServerPipe($@"LOCAL\{CommonValues.PipeName}");
                    ShowInfoBar("正在等待客户端......", InfoBarSeverity.Informational, false);
                    break;
                case PipeMode.Client:
                    pipe = new ClientPipe(CommonValues.PipeName);
                    ShowInfoBar("正在连接......", InfoBarSeverity.Informational, false);
                    break;
                default:
                    throw new InvalidOperationException("未知的管道模式。");
            }

            IsPipeCreated = true;
            

            Thread receiver = new(async () =>
            {
                try
                {
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                    CancellationTokenSource startCts = SelectedPipeMode switch
                    {
                        PipeMode.Client => CancellationTokenSource.CreateLinkedTokenSource(pipeCts.Token, timeout.Token),
                        _ => pipeCts
                    };

                    try
                    {
                        await pipe.StartAsync(startCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (timeout.IsCancellationRequested)
                        {
                            App.DispatcherQueue.TryEnqueue(async () =>
                            {
                                ShowInfoBar("连接超时", InfoBarSeverity.Error, true);
                                await ClosePipeAsync();
                            });
                        }
                        
                        return;
                    }

                    App.DispatcherQueue.TryEnqueue(() =>
                    {
                        IsPipeCreated = IsPipeStarted = true;
                        HideInfoBar();
                    });

                    Thread keepAlive = new(() =>
                    {
                        while (!pipeCts.Token.IsCancellationRequested)
                        {
                            if (!pipe.IsConnected)
                            {
                                pipeCts.Cancel();
                                App.DispatcherQueue.TryEnqueue(async () =>
                                {
                                    await ClosePipeAsync();
                                    ShowInfoBar("管道已关闭", InfoBarSeverity.Error, true);
                                });
                                break;
                            }
                            else
                            {
                                Thread.Sleep(200);
                            }
                        }
                    });

                    keepAlive.Start();

                    while (!pipeCts.Token.IsCancellationRequested)
                    {
                        string? message = await pipe.ReadAsync(pipeCts.Token);

                        if (!string.IsNullOrEmpty(message))
                        {
                            Message msg = new(MessageType.Inbound, message);
                            App.DispatcherQueue.TryEnqueue(() => Messages.Add(msg));
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException)
                {
                }
            });
            receiver.Start();
        }

        Messages.Clear();
    }

    private async Task ClosePipeAsync()
    {
        IsPipeCreated = false;
        IsPipeStarted = false;
        if (pipe is not null)
        {
            await pipe.DisposeAsync();
            pipe = null;
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (IsPipeStarted && !string.IsNullOrWhiteSpace(ToBeSentMessage))
        {
            try
            {
                await pipe.WriteAsync(ToBeSentMessage, CancellationToken.None);
                Messages.Add(new Message(MessageType.Outbound, ToBeSentMessage));
            }
            catch (IOException)
            {
                await ClosePipeAsync();
                ShowInfoBar("管道已关闭", InfoBarSeverity.Error, true);
            }
        }

        ToBeSentMessage = string.Empty;
    }

    private void ShowInfoBar(string title, InfoBarSeverity severity, bool closable)
    {
        InfoBarTitle = title;
        InfoBarSeverity = severity;
        InfoBarVisible = true;
        InfoBarClosable = closable;
    }

    private void HideInfoBar()
    {
        InfoBarVisible = false;
    }
}
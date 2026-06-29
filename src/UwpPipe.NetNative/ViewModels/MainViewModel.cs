using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using UwpPipe.Common;
using UwpPipe.Common.Messages;
using UwpPipe.Common.Pipes;

namespace UwpPipe.NetNative.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private PipeBase pipe;
    private CancellationTokenSource pipeCts = new();

    public PipeMode[] PipeModes { get; } = [PipeMode.Client, PipeMode.Server];
    public string StartOrStopPipeButtonString { get => IsPipeCreated ? "停止" : "启动"; }
    public bool IsConnectToClient { get => SelectedPipeMode == PipeMode.Client; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectToClient))]
    private PipeMode selectedPipeMode;

    [ObservableProperty]
    private bool isPipeStarted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartOrStopPipeButtonString))]
    private bool isPipeCreated;

    [ObservableProperty]
    private ObservableCollection<Message> messages = [];

    [ObservableProperty]
    private string toBeSentMessage;

    [ObservableProperty]
    private bool infoBarVisible;

    [ObservableProperty]
    private string infoBarTitle;

    [ObservableProperty]
    private InfoBarSeverity infoBarSeverity;

    [ObservableProperty]
    private bool infoBarClosable;

    [ObservableProperty]
    private bool clientConnectToAnotherUwpServer;

    [ObservableProperty]
    private string targetUwpServerPackageFamilyName;

    [ObservableProperty]
    private bool serverAllowAnotherUwpClient;

    [ObservableProperty]
    private string targetUwpClientPackageFamilyName;

    [RelayCommand]
    private async Task StartOrStopPipe()
    {
        if (IsPipeCreated)
        {
            pipeCts.Cancel();
            await ClosePipeAsync();
            HideInfoBar();
        }
        else
        {
            switch (SelectedPipeMode)
            {
                case PipeMode.Server:
                    string serverPipeName = $@"LOCAL\{CommonValues.PipeName}";

                    if (ServerAllowAnotherUwpClient)
                    {
                        string sid = CommonValues.TryGetPackageSid(TargetUwpClientPackageFamilyName);
                        if (string.IsNullOrWhiteSpace(sid))
                        {
                            ShowInfoBar("包系列名称无效。", InfoBarSeverity.Error, true);
                            return;
                        }

                        PipeSecurity access = new();
                        SecurityIdentifier appSid = new(sid);
                        SecurityIdentifier currentUserSid = WindowsIdentity.GetCurrent().Owner ?? throw new InvalidOperationException("不能获取到当前进程的 SID。");

                        PipeAccessRights rights = PipeAccessRights.Read | PipeAccessRights.Write;
                        access.AddAccessRule(new PipeAccessRule(appSid, rights, AccessControlType.Allow));
                        access.AddAccessRule(new PipeAccessRule(currentUserSid, rights, AccessControlType.Allow));
                        NamedPipeServerStream pipeStream = NamedPipeServerStreamConstructors.New(serverPipeName,
                            PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous, 512, 512, access, HandleInheritability.None);
                        pipe = new ServerPipe(pipeStream);
                    }
                    else
                    {
                        pipe = new ServerPipe(serverPipeName);
                    }
                    ShowInfoBar("正在等待客户端......", InfoBarSeverity.Informational, false);
                    break;
                case PipeMode.Client:
                    string targetPipeName;

                    if (ClientConnectToAnotherUwpServer)
                    {
                        string sid = CommonValues.TryGetPackageSid(TargetUwpServerPackageFamilyName);
                        if (string.IsNullOrWhiteSpace(sid))
                        {
                            ShowInfoBar("包系列名称无效。", InfoBarSeverity.Error, true);
                            return;
                        }

                        int sessionId = CommonValues.GetCurrentSessionIdNative() ?? throw new InvalidOperationException("无法获取当前进程的 SessionId。");
                        targetPipeName = CommonValues.GetPipeNameForPackagedApps(sessionId, sid, CommonValues.PipeName);
                    }
                    else
                    {
                        targetPipeName = CommonValues.PipeName;
                    }

                    pipe = new ClientPipe(targetPipeName);
                    ShowInfoBar("正在连接......", InfoBarSeverity.Informational, false);
                    break;
                default:
                    throw new InvalidOperationException("未知的管道模式。");
            }

            pipeCts.Cancel();
            pipeCts = new();
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
                        ShowInfoBar("连接成功", InfoBarSeverity.Success, true);
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
                        string message = await pipe.ReadAsync(pipeCts.Token);

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
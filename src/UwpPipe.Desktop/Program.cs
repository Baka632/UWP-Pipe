using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using UwpPipe.Common;
using UwpPipe.Common.Pipes;
using Windows.Win32;
using Windows.Win32.Security;

CancellationTokenSource cts = new();
Console.CancelKeyPress += (sender, args) =>
{
    args.Cancel = true;
    cts.Cancel();
};

while (true)
{
    Console.WriteLine("Baka's Pipe Samples");
    Console.WriteLine("请选择：");
    Console.WriteLine("(0)作为服务器；(1)作为客户端；<其他按键>退出");

    string? choice = Console.ReadLine();
    if (int.TryParse(choice, out int choiceNumber) && (choiceNumber == 0 || choiceNumber == 1))
    {
        PipeMode mode = (PipeMode)choiceNumber;

        string? packageSid;
        while (true)
        {
            Console.WriteLine("请输入目标程序的包系列名称（Family Name）：");
            string? packageFamilyName = Console.ReadLine();
            packageSid = TryGetPackageSid(packageFamilyName);
            if (packageSid == null)
            {
                Console.WriteLine("名称无效。");
            }
            else
            {
                break;
            }
        }

        cts.Cancel();
        cts.Dispose();
        cts = new();
        PipeBase pipe;
        switch (mode)
        {
            case PipeMode.Server:
                PipeSecurity access = new();
                SecurityIdentifier appSid = new(packageSid);
                SecurityIdentifier currentUserSid = WindowsIdentity.GetCurrent().Owner ?? throw new InvalidOperationException("不能获取到当前进程的 SID。");

                PipeAccessRights rights = PipeAccessRights.Read | PipeAccessRights.Write;
                access.AddAccessRule(new PipeAccessRule(appSid, rights, AccessControlType.Allow));
                access.AddAccessRule(new PipeAccessRule(currentUserSid, rights, AccessControlType.Allow));
                NamedPipeServerStream pipeStream = NamedPipeServerStreamAcl.Create(
                    CommonValues.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 512, 512,
                    access, HandleInheritability.None);
                pipe = new ServerPipe(pipeStream);
                break;
            case PipeMode.Client:
                string pipeName = CommonValues.GetPipeNameForPackagedApps(packageSid);
                pipe = new ClientPipe(pipeName);
                break;
            default:
                Console.Clear();
                continue;
        }

        try
        {
            Console.WriteLine("等待中......");
            await pipe.StartAsync(cts.Token);
        }
        catch (Exception ex)
        {
            await pipe.DisposeAsync();
            Console.WriteLine("连接失败。");
            Console.WriteLine(ex);
            Console.WriteLine("按任意键继续......");
            Console.ReadKey();
            Console.Clear();
            continue;
        }

        Console.WriteLine("已连接。");
        (Thread sender, Thread receiver, Thread keepAlive) = GetThread(pipe, mode, cts);
        sender.Start();
        receiver.Start();
        keepAlive.Start();

        try
        {
            await Task.Delay(-1, cts.Token);
        }
        catch (TaskCanceledException)
        {
        }

        await pipe.DisposeAsync();
        Console.WriteLine("连接已断开。\n按任意键继续......");
        Console.ReadKey();
    }
    else
    {
        break;
    }

    Console.Clear();
}

static string? TryGetPackageSid(string? packageFamilyName)
{
    if (!string.IsNullOrWhiteSpace(packageFamilyName)
        && PInvoke.DeriveAppContainerSidFromAppContainerName(packageFamilyName, out PSID psid).Succeeded)
    {
        string packageSid;
        unsafe
        {
            SecurityIdentifier appSid = new((nint)psid.Value);
            packageSid = appSid.ToString();
            PInvoke.FreeSid(psid);
        }
        return packageSid;
    }
    else
    {
        return null;
    }
}

static (Thread Sender, Thread Receiver, Thread KeepAlive) GetThread(PipeBase pipe, PipeMode mode, CancellationTokenSource cts)
{
    Thread sender = new(async () =>
    {
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                string? input = Console.ReadLine();

                if (!string.IsNullOrEmpty(input))
                {
                    await pipe.WriteAsync(input, cts.Token);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            OnPipeDisconnected(cts);
        }
    });

    Thread receiver = new(async () =>
    {
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                string? message = await pipe.ReadAsync(cts.Token);
                if (!string.IsNullOrEmpty(message))
                {
                    Console.WriteLine($"【接收】{message}");
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            OnPipeDisconnected(cts);
        }
    });

    Thread keepAlive = new(() =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            if (!pipe.IsConnected)
            {
                OnPipeDisconnected(cts);
                break;
            }
            else
            {
                Thread.Sleep(200);
            }
        }
    });

    return (sender, receiver, keepAlive);
}

static void OnPipeDisconnected(CancellationTokenSource cts)
{
    cts.Cancel();
}
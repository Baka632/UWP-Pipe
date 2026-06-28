# UWP Pipe

本项目是一个演示 UWP（包括使用 .NET 10 的 UWP）与桌面应用程序进行管道通信的项目。

## 截图

![使用 NativeAOT 的 UWP 与桌面应用程序的管道连接](assets/native-aot.png)

![使用 .NET Native 的 UWP 与桌面应用程序的管道连接](assets/net-native.png)

## 技术要点

### 通用操作：获取打包应用 SID（安全描述符）

本项目通过打包应用的包系列名称（Family Name）获取 SID。

包系列名称可在 `Package.appxmanifest` 的“打包”一栏中找到。

![在 Package.appxmanifest 的“打包”一栏中获取包系列名称](assets/get-package-family-name.png)

然后，将包系列名称传递给 [DeriveAppContainerSidFromAppContainerName](https://learn.microsoft.com/windows/win32/api/userenv/nf-userenv-deriveappcontainersidfromappcontainername) 函数，便可获取到打包应用的 SID。

本项目使用 CsWin32 生成调用 `DeriveAppContainerSidFromAppContainerName` P/Invoke 代码。

获取到的结果是一个指针，可以像下面这样将其转换为 `SecurityIdentifier` 对象及字符串，以便后续使用：
```csharp
// 目标 UWP 应用的包系列名称。
string packageFamilyName = "Baka632.UwpPipe.NativeAot_xrr5xv0r1z0t2";
// 下面的 P/Invoke 代码由 CsWin32 生成。
PInvoke.DeriveAppContainerSidFromAppContainerName(packageFamilyName, out PSID psid).ThrowOnFailure();
unsafe
{
    SecurityIdentifier packageSid = new((nint)psid.Value);
    string packageSidSddl = packageSid.ToString();
    // 获取的 PSID 使用完后需要调用 FreeSid 函数释放，这不会影响已经创建好的 SecurityIdentifier 对象。
    PInvoke.FreeSid(psid);
}
```

### UWP 程序创建管道时

UWP 程序创建管道时，名称应当以 `LOCAL\` 开头，例如：
```csharp
string pipeName = "Baka632-Pipe"; // 管道名称。
NamedPipeServerStream stream = new NamedPipeServerStream($@"LOCAL\{pipeName}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
```

桌面应用程序读取管道时，需要获取 UWP 进程的 SID，然后像下面这样构造管道名称（这里管道名称不需要加 `LOCAL\`）：
```csharp
SecurityIdentifier packageSid; // 先前获取的打包应用 SID。
string pipeName = "Baka632-Pipe"; // 管道名称。
int sessionId = Process.GetCurrentProcess().SessionId;
string pipeFullName = $@"Sessions\{sessionId}\AppContainerNamedObjects\{packageSid}\{pipeName}";
NamedPipeClientStream client = new(".", pipeFullName, PipeDirection.InOut, PipeOptions.Asynchronous);
```

之后便可正常通信。

### 桌面程序创建管道时

桌面程序创建管道时，需要配置访问控制列表（ACL）以允许 UWP 访问管道。
可通过 `NamedPipeServerStreamAcl` 类创建带有 ACL 的 `NamedPipeServerStream`。
配置时，需要同时使用打包应用的 SID 和当前用户的 SID 来授予访问权限。

```csharp
PipeSecurity access = new();
SecurityIdentifier packageSid; // 先前获取的打包应用 SID。
string pipeName = "Baka632-Pipe"; // 管道名称。
SecurityIdentifier currentUserSid = WindowsIdentity.GetCurrent().Owner ?? throw new InvalidOperationException("不能获取到当前进程的 SID。");

// 权限可根据需要配置，本项目配置的是允许读写。
PipeAccessRights rights = PipeAccessRights.Read | PipeAccessRights.Write;
access.AddAccessRule(new PipeAccessRule(packageSid, rights, AccessControlType.Allow));
access.AddAccessRule(new PipeAccessRule(currentUserSid, rights, AccessControlType.Allow));
NamedPipeServerStream pipeStream = NamedPipeServerStreamAcl.Create(
    pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous, 512, 512,
    access, HandleInheritability.None);
```

UWP 程序直接打开管道即可，管道名称不需要加 `LOCAL\`。
```csharp
string pipeName = "Baka632-Pipe"; // 管道名称。
NamedPipeClientStream stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
```

之后便可正常通信。

## 许可

MIT 许可证。
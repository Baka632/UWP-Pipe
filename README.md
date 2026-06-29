[English](README-EN.md)

# UWP Pipe

本项目是一个演示 UWP（包括使用 .NET 10 的 UWP）与其他进程（包括桌面应用和其他 UWP 应用）进行管道通信的项目。

## 截图

![使用 NativeAOT 的 UWP 与桌面应用程序的管道连接](assets/native-aot.png)

![使用 .NET Native 的 UWP 与桌面应用程序的管道连接](assets/net-native.png)

![两个不同 UWP 进程之间的管道连接](assets/two-uwp-app-pipe.png)

## 技术要点

### 通用操作：获取打包应用 SID（安全描述符）

本项目通过打包应用的包系列名称（Family Name）获取 SID。

包系列名称可在 `Package.appxmanifest` 的“打包”一栏中找到。

![在 Package.appxmanifest 的“打包”一栏中获取包系列名称](assets/get-package-family-name.png)

然后，将包系列名称传递给 [DeriveAppContainerSidFromAppContainerName](https://learn.microsoft.com/windows/win32/api/userenv/nf-userenv-deriveappcontainersidfromappcontainername) 函数，便可获取到打包应用的 SID。

本项目使用 CsWin32 生成调用 `DeriveAppContainerSidFromAppContainerName` 的 P/Invoke 代码。

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

#### 针对基于 .NET Native UWP 应用的特殊说明

基于 .NET Native 的 UWP 应用自身不带 `SecurityIdentifier` 类，因此我们需要安装 `System.Security.Principal.Windows` 包。

但是，如果我们引用 `System.Security.Principal.Windows` 的最新版本 5.0.0，那么会产生依赖冲突，这会导致应用崩溃。

因此，我们需要退而求其次，引用 `System.Security.Principal.Windows` 的 4.7.0 版本，这样才不会出现问题。

### 创建管道

桌面应用与 UWP 应用创建管道的方法类似，都使用 `NamedPipeServerStream` 进行创建。

但是，UWP 应用创建管道时，管道名称应当以 `LOCAL\` 开头，例如：

```csharp
string pipeName = "Baka632-Pipe"; // 管道名称。
NamedPipeServerStream stream = new NamedPipeServerStream($@"LOCAL\{pipeName}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
```

这种方式创建的管道，桌面应用访问时不会出现权限问题，但其他 UWP 应用不能访问，因此需要为这个管道配置访问控制列表（ACL）。

可通过 `NamedPipeServerStreamAcl` 类创建带有 ACL 的 `NamedPipeServerStream`。

配置时，需要同时使用打包应用的 SID 和当前用户的 SID 来授予访问权限。

```csharp
PipeSecurity access = new();
SecurityIdentifier packageSid; // 先前获取的打包应用 SID。
string pipeName; // 管道名称。如果是 UWP 创建管道，则需要添加“LOCAL\”前缀。
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

#### 针对基于 .NET Native UWP 应用的特殊说明

 `PipeSecurity`、`PipeAccessRights`、`NamedPipeServerStreamAcl` 等成员在 .NET Native UWP 应用中不可用。

一个替代品是使用 `NamedPipeServerStream.NetFrameworkVersion` 包，其会帮我们引用必要的包来解决大部分成员缺失的问题。

不过，`NamedPipeServerStreamAcl` 仍然不可用，这时我们可以用此包提供的 `NamedPipeServerStreamConstructors.New` 方法构造带有 ACL 的管道。

另外，`NamedPipeServerStream.NetFrameworkVersion` 包应当使用 1.0.10 版本，原因同样是 `System.Security.Principal.Windows` 的版本问题。

### 访问管道

如果管道是桌面应用创建的，那么无论是桌面应用还是 UWP 应用，直接打开管道即可（管道名称不需要加 `LOCAL\`）。

```csharp
string pipeName = "Baka632-Pipe"; // 管道名称。
NamedPipeClientStream stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
```

如果管道是 UWP 应用创建的，则需要获取这个 UWP 包的 SID，然后像下面这样构造管道名称（管道名称同样不需要加 `LOCAL\`）：

```csharp
SecurityIdentifier packageSid; // 先前获取的打包应用 SID。
string pipeName = "Baka632-Pipe"; // 管道名称。
int sessionId = Process.GetCurrentProcess().SessionId;
string pipeFullName = $@"Sessions\{sessionId}\AppContainerNamedObjects\{packageSid}\{pipeName}";
NamedPipeClientStream client = new(".", pipeFullName, PipeDirection.InOut, PipeOptions.Asynchronous);
```

之后便可正常通信。

#### 针对基于 .NET Native UWP 应用的特殊说明

在基于 .NET Native UWP 应用中，通过 `Process.GetCurrentProcess().SessionId` 属性来获取 SessionId 是不可行的，尽管存在这个 API，但一旦调用便会无条件抛出异常（平台不支持）。

我们需要通过 P/Invoke 调用 `GetCurrentProcessId` 函数获取当前进程的 PID，然后调用 `ProcessIdToSessionId` 获取 SessionId：

```csharp
public static int? GetCurrentSessionIdNative()
{
    uint pid = PInvoke.GetCurrentProcessId();

    return PInvoke.ProcessIdToSessionId(pid, out uint sessionId)
        ? (int)sessionId
        : null;
}
```

## 致谢

这些内容为本项目带来了帮助：

- [hannesne/NamedPipesSample](https://github.com/hannesne/NamedPipesSample)

## 许可

MIT 许可证。

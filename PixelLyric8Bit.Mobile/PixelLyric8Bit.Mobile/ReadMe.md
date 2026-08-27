# Getting Started

Welcome to the Uno Platform!

To discover how to get started with your new app: https://aka.platform.uno/get-started

For more information on how to use the Uno.Sdk or upgrade Uno Platform packages in your solution: https://aka.platform.uno/using-uno-sdk

## 手动 `adb install` 一个 Debug 版 APK 会立刻闪退

真机上踩过一次：`dotnet build -f net10.0-android`（默认 Debug 配置）编译出来的 APK，直接 `adb install` 装上去一启动就崩，`logcat` 里能看到：

```
monodroid: No assemblies found in '.../.__override__/arm64-v8a' ... Assuming this is part of Fast Deployment. Exiting...
Fatal signal 6 (SIGABRT)
```

原因：.NET for Android 的 Debug 配置默认开着 "Fast Deployment"，代码集（DLL）不打进 APK 里，指望用 `dotnet build -t:Run`（或者 IDE 的部署流程）在装完之后再单独推一份代码集过去。单纯 `adb install` 一个 Debug APK 的话，代码集根本没跟着装上去，进程一启动找不到自己的代码就直接 `SIGABRT`。

两种解法：
- **想用 `adb install` 手动装**：编译 Release 配置——`dotnet build -f net10.0-android -c Release`，代码集会老老实实打进 APK，装完直接能跑。
- **想要正常的开发迭代（改代码马上装到设备上验证）**：用 `dotnet build -t:Run -f net10.0-android`（或者 IDE 的运行按钮），这条命令自己会处理好 Fast Deploy 那份代码集的同步，不用手动 `adb install`。
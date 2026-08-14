using System.Runtime.CompilerServices;
using System.Windows;

// 单元测试项目要能访问 LrcParser / KaraokeTiming / LyricsFetcher 这些 internal 类
// （它们特意没标 public——不是给外部消费的 API，只是不想在测试项目里把每个类都改成 public）。
[assembly: InternalsVisibleTo("PixelLyric8BitFix.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

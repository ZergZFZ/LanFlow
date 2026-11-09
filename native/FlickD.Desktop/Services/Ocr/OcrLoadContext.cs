using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace FlickD.Desktop.Services.Ocr;

/// <summary>
/// OCR 插件的加载上下文：托管依赖从插件目录解析，原生库同样限定在 runtime/ 内。
/// 非可回收——引擎生命周期与进程一致，避免卸载时的原生库悬挂。
/// </summary>
public sealed class OcrLoadContext : AssemblyLoadContext
{
    private readonly string _runtimeDirectory;
    private readonly AssemblyDependencyResolver? _resolver;

    public OcrLoadContext(string runtimeDirectory) : base("FlickD.Ocr", isCollectible: false)
    {
        _runtimeDirectory = runtimeDirectory;

        var pluginPath = Path.Combine(runtimeDirectory, "FlickD.Ocr.dll");
        if (File.Exists(pluginPath) && File.Exists(Path.Combine(runtimeDirectory, "FlickD.Ocr.deps.json")))
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 契约程序集必须与宿主共用同一份，否则接口类型身份不一致导致转换失败。
        if (string.Equals(assemblyName.Name, "FlickD.Core", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_resolver?.ResolveAssemblyToPath(assemblyName) is { } resolved && File.Exists(resolved))
        {
            return LoadFromAssemblyPath(resolved);
        }

        if (assemblyName.Name is null) return null;
        var candidate = Path.Combine(_runtimeDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (_resolver?.ResolveUnmanagedDllToPath(unmanagedDllName) is { } resolved && File.Exists(resolved))
        {
            return LoadUnmanagedDllFromPath(resolved);
        }

        var candidate = Path.Combine(_runtimeDirectory, unmanagedDllName);
        if (!candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) candidate += ".dll";
        return File.Exists(candidate) ? LoadUnmanagedDllFromPath(candidate) : IntPtr.Zero;
    }
}

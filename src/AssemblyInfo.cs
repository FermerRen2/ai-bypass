using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("AiBypass")]
[assembly: AssemblyDescription("Обход геоблокировок для ИИ-сервисов и соцсетей через hosts и DNS")]
[assembly: AssemblyProduct("AiBypass")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: ComVisible(false)]

// Без этого атрибута рантайм считает сборку старой и включает TLS 1.0,
// из-за чего падают все https-запросы.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

using System.Reflection;

[assembly: AssemblyTitle("Overlay")]
[assembly: AssemblyProduct("Overlay")]
[assembly: AssemblyDescription("Horario, ping e FPS para Windows")]
[assembly: AssemblyVersion(Overlay.Versao.Atual + ".0")]
[assembly: AssemblyFileVersion(Overlay.Versao.Atual + ".0")]
[assembly: AssemblyInformationalVersion(Overlay.Versao.Atual)]

namespace Overlay
{
    static class Versao
    {
        public const string Atual = "0.1.0";
    }
}

using System;
using System.IO;

namespace Overlay
{
    // Onde ficam programa, config e log.
    // Instalado (C:\Program Files\Overlay, pasta que só administrador altera):
    //   - config.ini em %LOCALAPPDATA%\Overlay, para o Daniel editar sem admin. O overlay só LÊ dali (config é dado, nunca código).
    //   - overlay.log em Program Files\Overlay\log. O overlay roda elevado e nunca pode ESCREVER numa pasta que o usuário
    //     controla: uma junção ali faria o processo elevado escrever em arquivos do sistema.
    // Fora do Program Files (pasta de desenvolvimento E:\overlay): tudo ao lado do exe.
    static class Pastas
    {
        public static string Programa
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static bool Instalado
        {
            get
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\') + "\\";
                return Programa.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Só leitura quando instalado: não cria a pasta (quem cria é o instalar.ps1).
        public static string Config
        {
            get
            {
                if (!Instalado) return Programa;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Overlay");
            }
        }

        public static string Log
        {
            get
            {
                if (!Instalado) return Programa;
                string pasta = Path.Combine(Programa, "log");
                Directory.CreateDirectory(pasta);
                return pasta;
            }
        }
    }
}

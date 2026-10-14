using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Overlay
{
    // "Iniciar com o Windows" via Agendador de Tarefas: tarefa no logon do usuário com privilégio mais alto,
    // assim o overlay sobe como administrador (FPS e ping funcionando) sem pedir UAC a cada boot.
    static class InicioAutomatico
    {
        public const string NomeTarefa = "Overlay Horario Ping FPS";

        public static bool EstaAtivo()
        {
            return Schtasks("/Query /TN \"" + NomeTarefa + "\"") == 0;
        }

        // Criar tarefa com privilégio mais alto exige que o overlay esteja rodando como administrador.
        public static bool Ativar(string caminhoExe)
        {
            string xml = Path.Combine(Path.GetTempPath(), "overlay-tarefa.xml");
            try
            {
                File.WriteAllText(xml, GerarXml(caminhoExe, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
                return Schtasks("/Create /F /TN \"" + NomeTarefa + "\" /XML \"" + xml + "\"") == 0;
            }
            finally
            {
                try { File.Delete(xml); }
                catch (IOException) { }
            }
        }

        public static bool Desativar()
        {
            return Schtasks("/Delete /F /TN \"" + NomeTarefa + "\"") == 0;
        }

        // Os padrões do schtasks /SC ONLOGON matariam o overlay depois de 72 h e não o iniciariam na bateria; o XML evita isso.
        public static string GerarXml(string caminhoExe, string usuario)
        {
            string exe = SecurityElement.Escape(caminhoExe);
            string pasta = SecurityElement.Escape(Path.GetDirectoryName(caminhoExe));
            string user = SecurityElement.Escape(usuario);
            return
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Overlay de horario, ping e FPS (E:\\overlay).</Description></RegistrationInfo>\r\n" +
"  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
"    <Priority>7</Priority>\r\n" +
"  </Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><WorkingDirectory>" + pasta + "</WorkingDirectory></Exec></Actions>\r\n" +
"</Task>\r\n";
        }

        static int Schtasks(string argumentos)
        {
            var info = new ProcessStartInfo("schtasks.exe", argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(info))
            {
                string erro = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                // /Query com código != 0 só significa "tarefa não existe": não é erro.
                if (p.ExitCode != 0 && !argumentos.StartsWith("/Query"))
                    Log.Mensagem("schtasks " + argumentos + " falhou (" + p.ExitCode + "): " + erro.Trim());
                return p.ExitCode;
            }
        }
    }
}

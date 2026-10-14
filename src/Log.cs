using System;
using System.IO;

namespace Overlay
{
    static class Log
    {
        static readonly object trava = new object();

        public static string Caminho
        {
            get { return Path.Combine(Pastas.Log, "overlay.log"); }
        }

        public static void Erro(Exception ex)
        {
            Mensagem(ex == null ? "Erro desconhecido" : ex.ToString());
        }

        // Mesma mensagem só uma vez a cada 10 min: com o FC aberto as tentativas de 1 em 1 min encheriam o log.
        static readonly TimeSpan IntervaloRepeticao = TimeSpan.FromMinutes(10);
        static readonly System.Collections.Generic.Dictionary<string, DateTime> ultimaVez =
            new System.Collections.Generic.Dictionary<string, DateTime>();

        public static void Mensagem(string texto)
        {
            try
            {
                lock (trava)
                {
                    DateTime agora = DateTime.Now;
                    DateTime anterior;
                    if (ultimaVez.TryGetValue(texto, out anterior) && agora - anterior < IntervaloRepeticao) return;
                    if (ultimaVez.Count > 50) ultimaVez.Clear();
                    ultimaVez[texto] = agora;
                    File.AppendAllText(Caminho, agora.ToString("yyyy-MM-dd HH:mm:ss") + "  " + texto + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Sem log é melhor do que derrubar o overlay no meio do jogo.
            }
        }
    }
}

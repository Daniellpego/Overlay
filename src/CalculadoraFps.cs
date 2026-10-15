using System;
using System.Collections.Generic;
using System.Globalization;

namespace Overlay
{
    // Lógica pura (sem processo nem janela) para poder ser testada com CSV sintético em tests\TesteCalculadoraFps.cs.
    // Tempos em ms: CPUStartTimeInMs/CPUStartTime; TimeInMs para o CSV legado.
    class CalculadoraFps
    {
        public const double JanelaMs = 1000;
        public const double ValidadeMs = 2000;

        readonly object trava = new object();
        readonly Dictionary<string, Queue<double>> framesPorSwapchain = new Dictionary<string, Queue<double>>();
        int indicePid = -1;
        int indiceSwapchain = -1;
        int indiceTempo = -1;
        int maiorIndice = -1;
        volatile int pidAlvo;
        double ultimoTempoFrame;
        double ultimaChegadaMs = double.MinValue;
        double proximaLimpezaMs;

        public string ErroCabecalho { get; private set; }

        public void DefinirAlvo(int pid)
        {
            lock (trava)
            {
                if (pid == pidAlvo) return;
                pidAlvo = pid;
                framesPorSwapchain.Clear();
                ultimoTempoFrame = 0;
                ultimaChegadaMs = double.MinValue;
                proximaLimpezaMs = 0;
            }
        }

        public void ProcessarLinha(string linha, double agoraMs)
        {
            if (string.IsNullOrEmpty(linha)) return;

            if (linha.StartsWith("Application,", StringComparison.Ordinal))
            {
                LerCabecalho(linha.Split(','));
                return;
            }
            if (maiorIndice < 0) return;

            // PresentMon envia quadros de todos os processos: descartar outros PIDs antes de alocar strings.
            int inicioPid, tamanhoPid;
            int pid;
            if (!LocalizarCampo(linha, indicePid, out inicioPid, out tamanhoPid) ||
                !LerPid(linha, inicioPid, tamanhoPid, out pid) || pid == 0 || pid != pidAlvo) return;

            lock (trava)
            {
                if (pid != pidAlvo || pid == 0) return;

                int inicioTempo, tamanhoTempo, inicioSwapchain, tamanhoSwapchain;
                if (!LocalizarCampo(linha, indiceTempo, out inicioTempo, out tamanhoTempo) ||
                    !LocalizarCampo(linha, indiceSwapchain, out inicioSwapchain, out tamanhoSwapchain)) return;
                double tempo;
                if (!double.TryParse(linha.Substring(inicioTempo, tamanhoTempo), NumberStyles.Float, CultureInfo.InvariantCulture, out tempo) ||
                    double.IsNaN(tempo) || double.IsInfinity(tempo) || tempo < 0) return;

                Queue<double> fila;
                string swapchain = linha.Substring(inicioSwapchain, tamanhoSwapchain);
                if (!framesPorSwapchain.TryGetValue(swapchain, out fila))
                {
                    fila = new Queue<double>();
                    framesPorSwapchain[swapchain] = fila;
                }
                fila.Enqueue(tempo);
                while (fila.Count > 0 && fila.Peek() < tempo - JanelaMs) fila.Dequeue();

                if (tempo > ultimoTempoFrame) ultimoTempoFrame = tempo;
                PodarSuperficies();
                ultimaChegadaMs = agoraMs;
            }
        }

        void PodarSuperficies()
        {
            if (ultimoTempoFrame < proximaLimpezaMs) return;
            proximaLimpezaMs = ultimoTempoFrame + JanelaMs;
            List<string> vazias = null;
            foreach (KeyValuePair<string, Queue<double>> par in framesPorSwapchain)
            {
                Queue<double> fila = par.Value;
                while (fila.Count > 0 && fila.Peek() < ultimoTempoFrame - JanelaMs) fila.Dequeue();
                if (fila.Count == 0)
                {
                    if (vazias == null) vazias = new List<string>();
                    vazias.Add(par.Key);
                }
            }
            if (vazias != null) foreach (string chave in vazias) framesPorSwapchain.Remove(chave);
        }

        static bool LocalizarCampo(string linha, int indice, out int inicio, out int tamanho)
        {
            inicio = 0;
            tamanho = 0;
            for (int i = 0; i < indice; i++)
            {
                int separador = linha.IndexOf(',', inicio);
                if (separador < 0) return false;
                inicio = separador + 1;
            }
            int fim = linha.IndexOf(',', inicio);
            tamanho = (fim < 0 ? linha.Length : fim) - inicio;
            return true;
        }

        static bool LerPid(string linha, int inicio, int tamanho, out int pid)
        {
            pid = 0;
            int fim = inicio + tamanho;
            while (inicio < fim && char.IsWhiteSpace(linha[inicio])) inicio++;
            while (fim > inicio && char.IsWhiteSpace(linha[fim - 1])) fim--;
            if (inicio < fim && linha[inicio] == '+') inicio++;
            if (inicio == fim) return false;
            for (int i = inicio; i < fim; i++)
            {
                int digito = linha[i] - '0';
                if (digito < 0 || digito > 9 || pid > (int.MaxValue - digito) / 10) return false;
                pid = pid * 10 + digito;
            }
            return true;
        }

        // null = sem dados recentes do processo em primeiro plano (desktop, jogo em loading longo, PresentMon parado).
        public int? Fps(double agoraMs)
        {
            lock (trava)
            {
                if (agoraMs - ultimaChegadaMs > ValidadeMs) return null;

                // Um processo pode ter várias swapchains (launcher, overlay de terceiros); vale a que mais apresentou frames.
                double melhor = -1;
                int maiorAmostra = 0;
                foreach (Queue<double> fila in framesPorSwapchain.Values)
                {
                    if (fila.Count < 3) continue;
                    double primeiro = fila.Peek();
                    double ultimo = primeiro;
                    foreach (double t in fila) ultimo = t;

                    // Swapchain que parou de apresentar (fechou) não pode congelar o valor antigo.
                    if (ultimo < ultimoTempoFrame - JanelaMs) continue;
                    // Uma rajada curta de uma superfície auxiliar não representa o ritmo do jogo.
                    if (ultimo - primeiro < 250) continue;

                    double fps = (fila.Count - 1) * 1000.0 / (ultimo - primeiro);
                    if (fila.Count > maiorAmostra)
                    {
                        maiorAmostra = fila.Count;
                        melhor = fps;
                    }
                }
                if (melhor < 0) return null;
                return (int)Math.Round(melhor);
            }
        }

        void LerCabecalho(string[] colunas)
        {
            int pid = Array.IndexOf(colunas, "ProcessID");
            int swapchain = Array.IndexOf(colunas, "SwapChainAddress");
            int tempo = Array.IndexOf(colunas, "CPUStartTimeInMs");
            if (tempo < 0) tempo = Array.IndexOf(colunas, "CPUStartTime");
            if (tempo < 0) tempo = Array.IndexOf(colunas, "TimeInMs");

            lock (trava)
            {
                if (pid < 0 || swapchain < 0 || tempo < 0)
                {
                    maiorIndice = -1;
                    ErroCabecalho = "Colunas esperadas ausentes no CSV do PresentMon: " + string.Join(",", colunas);
                    return;
                }
                indicePid = pid;
                indiceSwapchain = swapchain;
                indiceTempo = tempo;
                maiorIndice = Math.Max(pid, Math.Max(swapchain, tempo));
                ErroCabecalho = null;
                framesPorSwapchain.Clear();
                ultimoTempoFrame = 0;
                proximaLimpezaMs = 0;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Overlay
{
    // Lógica pura: recebe "o processo X mandou um pacote UDP para o IP Y" e aponta o servidor da partida
    // (o IP público que mais recebeu pacotes do processo em primeiro plano nos últimos segundos).
    // Testada em tests\TesteRede.cs.
    class DetectorServidor
    {
        public const double JanelaMs = 5000;
        // Jogo em partida manda dezenas de pacotes por segundo; menos que isso é menu, chat de voz parado ou telemetria.
        public const int MinimoPacotesNaJanela = 20;

        readonly object trava = new object();
        readonly Dictionary<IPAddress, Queue<double>> pacotesPorIp = new Dictionary<IPAddress, Queue<double>>();
        volatile int pidAlvo;
        IPAddress candidato;
        double candidatoDesde;
        const double EstabilidadeMs = 2000;

        // Leitura sem trava para o callback ETW descartar cedo os pacotes de outros processos.
        public bool EhAlvo(int pid)
        {
            return pid != 0 && pid == pidAlvo;
        }

        public void DefinirAlvo(int pid)
        {
            lock (trava)
            {
                if (pid == pidAlvo) return;
                pidAlvo = pid;
                pacotesPorIp.Clear();
                candidato = null;
            }
        }

        public void RegistrarEnvio(int pid, IPAddress destino, double agoraMs)
        {
            if (destino == null || !EhIpPublico(destino)) return;
            lock (trava)
            {
                if (pid != pidAlvo || pid == 0) return;
                Queue<double> fila;
                if (!pacotesPorIp.TryGetValue(destino, out fila))
                {
                    fila = new Queue<double>();
                    pacotesPorIp[destino] = fila;
                }
                fila.Enqueue(agoraMs);
                Podar(fila, agoraMs);
            }
        }

        public IPAddress Servidor(double agoraMs)
        {
            lock (trava)
            {
                IPAddress melhor = null;
                int maisPacotes = MinimoPacotesNaJanela - 1;
                int segundo = 0;
                var vazios = new List<IPAddress>();
                foreach (KeyValuePair<IPAddress, Queue<double>> par in pacotesPorIp)
                {
                    Podar(par.Value, agoraMs);
                    if (par.Value.Count == 0) vazios.Add(par.Key);
                    else if (par.Value.Count > maisPacotes)
                    {
                        segundo = Math.Max(segundo, melhor == null ? 0 : maisPacotes);
                        maisPacotes = par.Value.Count;
                        melhor = par.Key;
                    }
                    else segundo = Math.Max(segundo, par.Value.Count);
                }
                foreach (IPAddress ip in vazios) pacotesPorIp.Remove(ip);
                // Volume sozinho não prova identidade. Destinos empatados não são confiáveis.
                if (melhor == null || maisPacotes < segundo * 1.25)
                {
                    candidato = null;
                    return null;
                }
                if (!melhor.Equals(candidato))
                {
                    candidato = melhor;
                    candidatoDesde = agoraMs;
                    return null;
                }
                return agoraMs - candidatoDesde >= EstabilidadeMs ? candidato : null;
            }
        }

        static void Podar(Queue<double> fila, double agoraMs)
        {
            while (fila.Count > 0 && fila.Peek() < agoraMs - JanelaMs) fila.Dequeue();
        }

        public static bool EhIpPublico(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return false;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = ip.GetAddressBytes();
                if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false; // CGNAT
                if (b[0] == 169 && b[1] == 254) return false;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
                if (b[0] == 192 && b[1] == 168) return false;
                if (b[0] >= 224) return false; // multicast, reservado e broadcast
                return true;
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
                byte[] b = ip.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return false; // fc00::/7 (rede local)
                if (ip.IsIPv4MappedToIPv6) return EhIpPublico(ip.MapToIPv4());
                return true;
            }
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Overlay
{
    enum OrigemPing { Nenhuma, ServidorDoJogo, Reserva }

    // Faz um ping ICMP por segundo: no servidor da partida quando detectado e respondendo; senão no alvo_ping do config.
    class MedidorPing : IDisposable
    {
        const int IntervaloMs = 1000;
        const int TimeoutMs = 900;
        // Servidores de jogo costumam bloquear ICMP; depois de 3 falhas seguidas usamos o alvo de reserva.
        const int FalhasAteDesistirDoServidor = 3;
        const int AmostrasNaMedia = 3;

        readonly Func<IPAddress> servidorDetectado;
        readonly Thread thread;
        readonly ManualResetEvent parar = new ManualResetEvent(false);
        readonly Queue<long> amostras = new Queue<long>();
        readonly object trava = new object();

        volatile string alvoReserva;
        IPAddress reservaResolvida;
        string reservaResolvidaDe;
        DateTime proximaResolucao = DateTime.MinValue;
        bool erroDnsRegistrado;

        IPAddress ultimoServidor;
        int falhasServidor;
        DateTime desistiuDoServidorEm;
        OrigemPing origemAtual = OrigemPing.Nenhuma;
        int? valorAtual;

        public MedidorPing(string alvoReserva, Func<IPAddress> servidorDetectado)
        {
            this.alvoReserva = alvoReserva;
            this.servidorDetectado = servidorDetectado;
            thread = new Thread(Loop) { IsBackground = true, Name = "Ping" };
        }

        public void Iniciar()
        {
            thread.Start();
        }

        public void DefinirAlvoReserva(string alvo)
        {
            alvoReserva = alvo;
        }

        public void Ler(out int? milissegundos, out OrigemPing origem)
        {
            lock (trava)
            {
                milissegundos = valorAtual;
                origem = origemAtual;
            }
        }

        void Loop()
        {
            using (var ping = new Ping())
            {
                while (!parar.WaitOne(0))
                {
                    var cronometro = Stopwatch.StartNew();
                    try
                    {
                        Medir(ping);
                    }
                    catch (Exception ex)
                    {
                        Log.Erro(ex);
                    }
                    int restante = IntervaloMs - (int)cronometro.ElapsedMilliseconds;
                    if (parar.WaitOne(Math.Max(restante, 50))) break;
                }
            }
        }

        void Medir(Ping ping)
        {
            IPAddress servidor = servidorDetectado == null ? null : servidorDetectado();
            if (servidor != null && !servidor.Equals(ultimoServidor))
            {
                ultimoServidor = servidor;
                falhasServidor = 0;
            }
            // Servidor que não respondia pode ter só perdido pacotes: tenta de novo de tempos em tempos.
            if (falhasServidor >= FalhasAteDesistirDoServidor && DateTime.UtcNow - desistiuDoServidorEm > TimeSpan.FromSeconds(30))
                falhasServidor = 0;

            if (servidor != null && falhasServidor < FalhasAteDesistirDoServidor)
            {
                long? rtt = Pingar(ping, servidor);
                if (rtt.HasValue)
                {
                    falhasServidor = 0;
                    Registrar(OrigemPing.ServidorDoJogo, rtt);
                    return;
                }
                if (++falhasServidor == FalhasAteDesistirDoServidor) desistiuDoServidorEm = DateTime.UtcNow;
                // Ainda dentro da tolerância: mantém o último valor do servidor em vez de piscar para a reserva.
                if (falhasServidor < FalhasAteDesistirDoServidor && origemAtual == OrigemPing.ServidorDoJogo) return;
            }

            IPAddress reserva = ResolverReserva();
            Registrar(reserva == null ? OrigemPing.Nenhuma : OrigemPing.Reserva, reserva == null ? null : Pingar(ping, reserva));
        }

        static long? Pingar(Ping ping, IPAddress ip)
        {
            try
            {
                PingReply resposta = ping.Send(ip, TimeoutMs);
                if (resposta.Status == IPStatus.Success) return resposta.RoundtripTime;
            }
            catch (PingException) { }
            catch (SocketException) { }
            return null;
        }

        void Registrar(OrigemPing origem, long? rtt)
        {
            lock (trava)
            {
                if (origem != origemAtual)
                {
                    amostras.Clear();
                    origemAtual = origem;
                }
                if (!rtt.HasValue)
                {
                    // Uma perda isolada não apaga o valor; só a fila vazia (perdas seguidas) mostra "---".
                    if (amostras.Count > 0) amostras.Dequeue();
                }
                else
                {
                    amostras.Enqueue(rtt.Value);
                    while (amostras.Count > AmostrasNaMedia) amostras.Dequeue();
                }
                valorAtual = amostras.Count == 0 ? (int?)null : Mediana(amostras);
            }
        }

        static int Mediana(IEnumerable<long> valores)
        {
            var lista = new List<long>(valores);
            lista.Sort();
            return (int)lista[lista.Count / 2];
        }

        IPAddress ResolverReserva()
        {
            string alvo = alvoReserva;
            if (alvo != reservaResolvidaDe)
            {
                reservaResolvidaDe = alvo;
                reservaResolvida = null;
                proximaResolucao = DateTime.MinValue;
                erroDnsRegistrado = false;
            }
            if (DateTime.UtcNow < proximaResolucao) return reservaResolvida;

            IPAddress direto;
            if (IPAddress.TryParse(alvo, out direto))
            {
                reservaResolvida = direto;
                proximaResolucao = DateTime.MaxValue;
                return direto;
            }
            try
            {
                IPAddress[] enderecos = Dns.GetHostAddresses(alvo);
                IPAddress escolhido = null;
                foreach (IPAddress ip in enderecos)
                    if (escolhido == null || (ip.AddressFamily == AddressFamily.InterNetwork && escolhido.AddressFamily != AddressFamily.InterNetwork))
                        escolhido = ip;
                if (escolhido != null) reservaResolvida = escolhido;
                proximaResolucao = DateTime.UtcNow.AddMinutes(5);
            }
            catch (Exception ex)
            {
                if (!(ex is SocketException) && !(ex is ArgumentException)) throw;
                if (!erroDnsRegistrado) Log.Mensagem("Nao foi possivel resolver alvo_ping '" + alvo + "': " + ex.Message);
                erroDnsRegistrado = true;
                proximaResolucao = DateTime.UtcNow.AddSeconds(30);
            }
            return reservaResolvida;
        }

        public void Dispose()
        {
            parar.Set();
        }
    }
}

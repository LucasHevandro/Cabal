using System;
using System.Collections.Generic;

namespace Game.Shared.Net
{
    /// <summary>
    /// Estima o relógio do servidor a partir de Ping/Pong. Para cada resposta, supõe que o servidor carimbou o
    /// horário no meio da ida e volta. Usa a amostra de menor latência entre as últimas (a menos afetada por fila
    /// e jitter), o que dá uma estimativa estável.
    /// </summary>
    public sealed class ServerClock
    {
        private const int MaxSamples = 8;

        private readonly Queue<(double Rtt, double Offset)> _samples = new Queue<(double, double)>();

        /// <summary>Verdadeiro depois da primeira resposta.</summary>
        public bool IsSynced { get; private set; }

        /// <summary>Diferença estimada: relógio do servidor − relógio local.</summary>
        public double Offset { get; private set; }

        /// <summary>Ida e volta mais recente, em segundos.</summary>
        public double LastRtt { get; private set; }

        /// <summary>Menor ida e volta entre as amostras guardadas.</summary>
        public double BestRtt { get; private set; }

        /// <summary>Começa com uma estimativa grosseira (ex.: o horário do Welcome).</summary>
        public void Seed(double serverTime, double localNow)
        {
            if (IsSynced) return;
            Offset = serverTime - localNow;
        }

        /// <summary>Registra uma resposta: enviado em <paramref name="localSent"/>, recebido em <paramref name="localReceived"/>.</summary>
        public void AddSample(double localSent, double serverTime, double localReceived)
        {
            var rtt = Math.Max(0, localReceived - localSent);
            var offset = serverTime - (localSent + rtt / 2);
            LastRtt = rtt;

            _samples.Enqueue((rtt, offset));
            while (_samples.Count > MaxSamples) _samples.Dequeue();

            var best = (Rtt: double.MaxValue, Offset: 0.0);
            foreach (var sample in _samples)
            {
                if (sample.Rtt < best.Rtt) best = sample;
            }
            BestRtt = best.Rtt;
            Offset = best.Offset;
            IsSynced = true;
        }

        /// <summary>Horário estimado do servidor agora.</summary>
        public double ServerNow(double localNow) => localNow + Offset;

        /// <summary>Converte um horário do servidor para o relógio local.</summary>
        public double ToLocal(double serverTime) => serverTime - Offset;
    }
}

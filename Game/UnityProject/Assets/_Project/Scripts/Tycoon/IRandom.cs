using System;

namespace MorpheHouse.Tycoon
{
    /// <summary>난수 추상화. 테스트에서 시드 고정 구현을 주입한다.</summary>
    public interface IRandom
    {
        /// <summary>[0,1) 균등 난수.</summary>
        double NextDouble();

        /// <summary>정규분포 난수.</summary>
        double NextGaussian(double mean, double standardDeviation);
    }

    public sealed class SystemRandomAdapter : IRandom
    {
        private readonly Random _random;

        public SystemRandomAdapter(int seed)
        {
            _random = new Random(seed);
        }

        public double NextDouble()
        {
            return _random.NextDouble();
        }

        /// <summary>Box-Muller 변환.</summary>
        public double NextGaussian(double mean, double standardDeviation)
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = _random.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + standardDeviation * z;
        }
    }
}

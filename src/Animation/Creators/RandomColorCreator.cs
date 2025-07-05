using System;
using System.Windows.Media;

namespace Xaml.Effects.Toolkit.Animation.Creators
{
    public class RandomColorCreator : RandomCreator<Color>
    {
        public RandomColorCreator()
        {
            long tick = DateTime.Now.Ticks;
            _random = new Random((int)(tick & 0xffffffffL) | (int)(tick >> 32));
        }
        private Random _random;

        public override Color Next => Color.FromRgb((byte)_random.Next(255), (byte)_random.Next(255), (byte)_random.Next(255));
    }
}

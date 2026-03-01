using System.Collections;
using System.Collections.Generic;

namespace Core
{
    public readonly struct FlatArray2dItem<T>
    {
        public readonly int X;
        public readonly int Y;
        public readonly T Value;

        public FlatArray2dItem(int x, int y, T value)
        {
            X = x;
            Y = y;
            Value = value;
        }

        public void Deconstruct(out int x, out int y, out T value)
        {
            x = X;
            y = Y;
            value = Value;
        }
    }

    public class FlatArray2d<T> : IEnumerable<FlatArray2dItem<T>>
    {
        public FlatArray2d(int width, int height)
        {
            Inner = new T[width * height];
            Width = width;
            Height = height;
        }
        
        public FlatArray2d(T[] array, int width, int height)
        {
            Inner = array;
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        public T this[int x, int y]
        {
            get => Inner[x + y * Width];
            set => Inner[x + y * Width] = value;
        }

        public T[] Inner { get; }

        public IEnumerator<FlatArray2dItem<T>> GetEnumerator()
        {
            int i = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new FlatArray2dItem<T>(x, y, Inner[i++]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
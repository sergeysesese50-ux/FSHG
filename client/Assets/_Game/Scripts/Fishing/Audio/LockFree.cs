using System.Threading;

namespace Fishing.Core.Audio
{
    /// <summary>
    /// Тройной буфер без блокировок: один писатель (главный поток) публикует свежий снимок, один читатель
    /// (звуковой поток) берёт последний опубликованный. Ни выделений памяти, ни ожиданий: писатель пишет в свой слот
    /// и меняет его местами со средним, читатель меняет свой слот со средним, только если там свежее.
    /// Слоты писателя и читателя никогда не совпадают, поэтому значение не рвётся (T — структура без ссылок).
    /// </summary>
    public sealed class SnapshotBuffer<T> where T : struct
    {
        private const int Fresh = 4;
        private const int IndexMask = 3;

        private readonly T[] _slots = new T[3];
        private int _back;          // только писатель
        private int _middle = 1;    // индекс среднего слота | Fresh, если писатель положил новое
        private int _front = 2;     // только читатель

        /// <summary>Писатель: опубликовать снимок.</summary>
        public void Publish(in T value)
        {
            _slots[_back] = value;
            int previous = Interlocked.Exchange(ref _middle, _back | Fresh);
            _back = previous & IndexMask;
        }

        /// <summary>Читатель: последний опубликованный снимок (или прежний, если нового нет). true — снимок новый.</summary>
        public bool Read(out T value)
        {
            bool fresh = (Volatile.Read(ref _middle) & Fresh) != 0;
            if (fresh)
            {
                int previous = Interlocked.Exchange(ref _middle, _front);
                _front = previous & IndexMask;
            }
            value = _slots[_front];
            return fresh;
        }
    }

    /// <summary>
    /// Очередь одного писателя и одного читателя без блокировок (кольцо фиксированной длины, степень двойки).
    /// Полная очередь не ждёт: TryWrite возвращает false (событие теряется, Dropped растёт).
    /// </summary>
    public sealed class SpscQueue<T> where T : struct
    {
        private readonly T[] _items;
        private readonly int _mask;
        private int _write;
        private int _read;

        /// <summary>Сколько записей не влезло (пишет только писатель).</summary>
        public int Dropped { get; private set; }

        public SpscQueue(int capacityPowerOfTwo)
        {
            int size = 2;
            while (size < capacityPowerOfTwo)
                size <<= 1;
            _items = new T[size];
            _mask = size - 1;
        }

        /// <summary>Писатель.</summary>
        public bool TryWrite(in T item)
        {
            int w = _write;
            int next = (w + 1) & _mask;
            if (next == Volatile.Read(ref _read))
            {
                Dropped++;
                return false;
            }
            _items[w] = item;
            Volatile.Write(ref _write, next);
            return true;
        }

        /// <summary>Читатель.</summary>
        public bool TryRead(out T item)
        {
            int r = _read;
            if (r == Volatile.Read(ref _write))
            {
                item = default;
                return false;
            }
            item = _items[r];
            Volatile.Write(ref _read, (r + 1) & _mask);
            return true;
        }
    }
}

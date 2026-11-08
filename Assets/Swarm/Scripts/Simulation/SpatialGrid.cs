using UnityEngine;

namespace Swarm.Simulation
{
    /// <summary>
    /// Равномерная сетка с подсчётной сортировкой: пересборка O(n) за кадр, без аллокаций.
    /// Хранит индексы точек, отсортированные по ячейкам.
    /// </summary>
    public sealed class SpatialGrid
    {
        public readonly float CellSize;
        public readonly int Cols, Rows;
        readonly float _inv;
        readonly int[] _cellStart;   // Cols*Rows + 1
        readonly int[] _cellOf;      // ячейка точки
        readonly int[] _items;       // индексы точек по ячейкам
        int _count;

        public SpatialGrid(float worldW, float worldH, float cellSize, int capacity)
        {
            CellSize = cellSize;
            _inv = 1f / cellSize;
            Cols = Mathf.CeilToInt(worldW / cellSize) + 1;
            Rows = Mathf.CeilToInt(worldH / cellSize) + 1;
            _cellStart = new int[Cols * Rows + 1];
            _cellOf = new int[capacity];
            _items = new int[capacity];
        }

        int CellX(float x) { int c = (int)(x * _inv); return c < 0 ? 0 : (c >= Cols ? Cols - 1 : c); }
        int CellY(float y) { int c = (int)(y * _inv); return c < 0 ? 0 : (c >= Rows ? Rows - 1 : c); }

        public void Build(Vector2[] pos, int count)
        {
            _count = count;
            System.Array.Clear(_cellStart, 0, _cellStart.Length);
            for (int i = 0; i < count; i++)
            {
                int c = CellY(pos[i].y) * Cols + CellX(pos[i].x);
                _cellOf[i] = c;
                _cellStart[c + 1]++;
            }
            for (int c = 1; c < _cellStart.Length; c++) _cellStart[c] += _cellStart[c - 1];
            // _cellStart[c] сейчас = начало ячейки c; используем копию-курсор через _cellOf проход
            for (int i = 0; i < count; i++)
            {
                int c = _cellOf[i];
                _items[_cellStart[c]++] = i;
            }
            // после прохода _cellStart[c] = конец ячейки c; сдвигаем обратно
            for (int c = _cellStart.Length - 1; c > 0; c--) _cellStart[c] = _cellStart[c - 1];
            _cellStart[0] = 0;
        }

        /// <summary>Перебор кандидатов в квадрате radius вокруг p. Возвращает число записанных в buffer.</summary>
        public int Query(Vector2 p, float radius, int[] buffer)
        {
            int x0 = CellX(p.x - radius), x1 = CellX(p.x + radius);
            int y0 = CellY(p.y - radius), y1 = CellY(p.y + radius);
            int n = 0;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * Cols;
                for (int x = x0; x <= x1; x++)
                {
                    int c = row + x;
                    int end = _cellStart[c + 1];
                    for (int k = _cellStart[c]; k < end; k++)
                    {
                        if (n >= buffer.Length) return n;
                        buffer[n++] = _items[k];
                    }
                }
            }
            return n;
        }

        public int Count => _count;
    }
}

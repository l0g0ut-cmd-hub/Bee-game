using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Swarm.Simulation
{
    /// <summary>
    /// Снимок партии целиком: обходит граф объектов симуляции рефлексией и пишет все поля
    /// (числа, enum, Vector2, массивы, списки, вложенные объекты симуляции). Загрузка — в свежий GameSim
    /// с тем же конфигом и сидом: массивы заполняются на месте, объекты — рекурсивно.
    /// Защита от несовместимости: в заголовке хеш раскладки полей — после обновления игры старый снимок просто не грузится.
    /// Проверка: tools/simtest «snapshot» — сохранить посреди партии, загрузить, доиграть оба и сравнить побитно.
    /// </summary>
    public static class SimSnapshot
    {
        const int Magic = 0x53574D31;   // «SWM1»
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static string LastError;
        static readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();

        public static byte[] Save(GameSim sim)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Magic);
                w.Write(LayoutHash());
                WriteObject(w, sim.Cfg);
                WriteObject(w, sim);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Конфиг из снимка (нужен, чтобы создать GameSim до загрузки). null — снимок не подходит.</summary>
        public static SimConfig ReadConfig(byte[] data)
        {
            try
            {
                using (var r = new BinaryReader(new MemoryStream(data)))
                {
                    if (r.ReadInt32() != Magic || r.ReadInt32() != LayoutHash()) return null;
                    var cfg = new SimConfig();
                    ReadInto(r, cfg);
                    return cfg;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Загружает снимок в свежий GameSim (созданный с конфигом из ReadConfig).</summary>
        public static bool Load(byte[] data, GameSim sim)
        {
            try
            {
                using (var r = new BinaryReader(new MemoryStream(data)))
                {
                    if (r.ReadInt32() != Magic || r.ReadInt32() != LayoutHash()) return false;
                    ReadInto(r, new SimConfig());   // конфиг уже применён при создании
                    ReadInto(r, sim);
                    return true;
                }
            }
            catch (Exception e)
            {
                LastError = e.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ раскладка
        static FieldInfo[] FieldsOf(Type t)
        {
            if (_fields.TryGetValue(t, out var f)) return f;
            var list = new List<FieldInfo>();
            for (var tt = t; tt != null && tt != typeof(object); tt = tt.BaseType)
                foreach (var fi in tt.GetFields(Flags | BindingFlags.DeclaredOnly))
                    if (!fi.IsLiteral && !fi.IsStatic) list.Add(fi);
            list.Sort((a, b) => string.CompareOrdinal(a.DeclaringType.Name + "." + a.Name, b.DeclaringType.Name + "." + b.Name));
            f = list.ToArray();
            _fields[t] = f;
            return f;
        }

        static int _hash;
        static int LayoutHash()
        {
            if (_hash != 0) return _hash;
            unchecked
            {
                int h = 17;
                var seen = new HashSet<Type>();
                var stack = new Stack<Type>();
                stack.Push(typeof(GameSim));
                stack.Push(typeof(SimConfig));
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    if (!seen.Add(t)) continue;
                    h = h * 31 + Stable(t.Name);
                    foreach (var fi in FieldsOf(t))
                    {
                        h = h * 31 + Stable(fi.Name) * 7 + Stable(fi.FieldType.Name);
                        var et = fi.FieldType.IsArray ? fi.FieldType.GetElementType() : fi.FieldType;
                        if (et.IsGenericType) et = et.GetGenericArguments()[0];
                        if (IsSimType(et)) stack.Push(et);
                    }
                }
                _hash = h == 0 ? 1 : h;
                return _hash;
            }
        }

        static int Stable(string s)
        {
            unchecked { int h = 23; for (int i = 0; i < s.Length; i++) h = h * 31 + s[i]; return h; }
        }

        static bool IsSimType(Type t) => t.Namespace == "Swarm.Simulation" && !t.IsEnum && !t.IsPrimitive;

        // ------------------------------------------------------------------ запись
        static void WriteObject(BinaryWriter w, object o)
        {
            foreach (var fi in FieldsOf(o.GetType()))
                WriteValue(w, fi.FieldType, fi.GetValue(o), fi.Name);
        }

        static void WriteValue(BinaryWriter w, Type t, object v, string name)
        {
            if (t == typeof(int)) w.Write((int)v);
            else if (t == typeof(float)) w.Write((float)v);
            else if (t == typeof(bool)) w.Write((bool)v);
            else if (t == typeof(byte)) w.Write((byte)v);
            else if (t == typeof(uint)) w.Write((uint)v);
            else if (t == typeof(long)) w.Write((long)v);
            else if (t == typeof(ulong)) w.Write((ulong)v);
            else if (t == typeof(double)) w.Write((double)v);
            else if (t == typeof(short)) w.Write((short)v);
            else if (t == typeof(string)) { w.Write(v != null); if (v != null) w.Write((string)v); }
            else if (t.IsEnum) w.Write(Convert.ToInt32(v));
            else if (t == typeof(Vector2)) { var p = (Vector2)v; w.Write(p.x); w.Write(p.y); }
            else if (t.IsArray)
            {
                var arr = (Array)v;
                w.Write(arr == null ? -1 : arr.Length);
                if (arr == null) return;
                var et = t.GetElementType();
                for (int i = 0; i < arr.Length; i++) WriteValue(w, et, arr.GetValue(i), name);
            }
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)v;
                w.Write(list == null ? -1 : list.Count);
                if (list == null) return;
                var et = t.GetGenericArguments()[0];
                for (int i = 0; i < list.Count; i++) WriteValue(w, et, list[i], name);
            }
            else if (IsSimType(t))
            {
                w.Write(v != null);
                if (v != null) WriteObject(w, v);
            }
            else throw new NotSupportedException("снимок: поле " + name + " типа " + t.FullName);
        }

        // ------------------------------------------------------------------ чтение
        static void ReadInto(BinaryReader r, object o)
        {
            foreach (var fi in FieldsOf(o.GetType()))
            {
                object cur = fi.GetValue(o);
                object nv = ReadValue(r, fi.FieldType, cur);
                if (!ReferenceEquals(nv, cur) || fi.FieldType.IsValueType) fi.SetValue(o, nv);
            }
        }

        static object ReadValue(BinaryReader r, Type t, object cur)
        {
            if (t == typeof(int)) return r.ReadInt32();
            if (t == typeof(float)) return r.ReadSingle();
            if (t == typeof(bool)) return r.ReadBoolean();
            if (t == typeof(byte)) return r.ReadByte();
            if (t == typeof(uint)) return r.ReadUInt32();
            if (t == typeof(long)) return r.ReadInt64();
            if (t == typeof(ulong)) return r.ReadUInt64();
            if (t == typeof(double)) return r.ReadDouble();
            if (t == typeof(short)) return r.ReadInt16();
            if (t == typeof(string)) return r.ReadBoolean() ? r.ReadString() : null;
            if (t.IsEnum) return Enum.ToObject(t, r.ReadInt32());
            if (t == typeof(Vector2)) return new Vector2(r.ReadSingle(), r.ReadSingle());
            if (t.IsArray)
            {
                int n = r.ReadInt32();
                if (n < 0) return null;
                var et = t.GetElementType();
                var arr = cur as Array;
                if (arr == null || arr.Length != n) arr = Array.CreateInstance(et, n);   // массив заполняем на месте, если размер совпал
                for (int i = 0; i < n; i++) arr.SetValue(ReadValue(r, et, arr.GetValue(i)), i);
                return arr;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                int n = r.ReadInt32();
                if (n < 0) return null;
                var et = t.GetGenericArguments()[0];
                var list = cur as IList ?? (IList)Activator.CreateInstance(t);
                var old = new List<object>();
                foreach (var x in list) old.Add(x);
                list.Clear();
                for (int i = 0; i < n; i++) list.Add(ReadValue(r, et, i < old.Count ? old[i] : null));
                return list;
            }
            if (IsSimType(t))
            {
                if (!r.ReadBoolean()) return null;
                object o = cur ?? Activator.CreateInstance(t, true);
                ReadInto(r, o);
                return o;
            }
            throw new NotSupportedException("снимок: тип " + t.FullName);
        }
    }
}

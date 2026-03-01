using R3;

namespace Extensions
{
    public static class ReactiveExtensions
    {
        public static void ApplyInnerMutations<T>(this ReactiveProperty<T> property)
        {
            property.OnNext(property.CurrentValue);
        }

        public static Observable<(T1, T2)> CombineLatestWith<T1, T2>(
            this Observable<T1> a,
            Observable<T2> b
        )
        {
            return a.CombineLatest(b, (x, y) => (x, y));
        }

        public static Observable<(T1, T2, T3)> CombineLatestWith<T1, T2, T3>(
            this Observable<(T1, T2)> ab,
            Observable<T3> c
        )
        {
            return ab.CombineLatest(c, (xy, z) => (xy.Item1, xy.Item2, z));
        }

        public static Observable<(T1, T2, T3, T4)> CombineLatestWith<T1, T2, T3, T4>(
            this Observable<(T1, T2, T3)> abc,
            Observable<T4> d
        )
        {
            return abc.CombineLatest(d, (xyz, w) => (xyz.Item1, xyz.Item2, xyz.Item3, w));
        }
    }
}
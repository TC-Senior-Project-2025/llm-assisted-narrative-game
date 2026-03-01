namespace Extensions {
    public static class NumberExtensions {
        public static string FormatWithSuffix(this int num) {
            return num switch {
                >= 1000000 => (num / 1000000D).ToString("0.#") + "M",
                >= 100000 => (num / 1000D).ToString("0.#") + "K",
                _ => num.ToString("#,0")
            };
        }
    }
}
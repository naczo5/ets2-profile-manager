using System.IO;

namespace Ets2ProfileManager
{
    static class StringExtensions
    {
        public static IEnumerable<String> SplitInParts(this String s, Int32 partLength)
        {
            ArgumentNullException.ThrowIfNull(s);
            if (partLength <= 0)
            {
                throw new ArgumentException("Part length has to be positive.", nameof(partLength));
            }

            for (int i = 0; i < s.Length; i += partLength)
            {
                yield return s.Substring(i, Math.Min(partLength, s.Length - i));
            }
        }

        public static string DirectoryToScsUsername(this string directorystring)
        {
            DirectoryInfo di = new(directorystring);
            IEnumerable<string> parts = di.Name.SplitInParts(2);
            string hexstring = String.Join(" ", parts);
            string[] hexValuesSplit = hexstring.Split(' ');
            string username = string.Empty;
            foreach (string hex in hexValuesSplit)
            {
                int value = Convert.ToInt32(hex, 16);
                char charValue = (char)value;
                username += charValue;
            }
            return username;
        }

        public static string ScsUsernameToDirectory(this string username)
        {
            char[] values = username.ToCharArray();
            string ScSDirectoryname = string.Empty;
            foreach (char letter in values)
            {
                int value = Convert.ToInt32(letter);
                string hex = $"{value:X}";
                ScSDirectoryname += hex;
            }
            return ScSDirectoryname;
        }

        public static bool IsEncodableUsername(string username, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(username))
            {
                reason = "Name must not be empty.";
                return false;
            }
            if (username.Any(c => char.IsControl(c) || c > 0xFF))
            {
                reason = "Name contains characters the game cannot store in profile folder IDs.";
                return false;
            }
            if (username.ScsUsernameToDirectory().Length % 2 != 0)
            {
                reason = "Name encodes to an invalid folder ID. Try a different name.";
                return false;
            }
            return true;
        }

        public static bool IsHex(this IEnumerable<char> chars)
        {
            bool isHex;
            foreach (char c in chars)
            {
                isHex = ((c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F'));

                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

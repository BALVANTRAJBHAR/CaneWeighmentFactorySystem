/// Small offline phonetic transliterator for master data entry.
/// It intentionally keeps existing Hindi text untouched and is only a starting
/// suggestion; operators can switch to manual mode and correct every value.
class HindiTransliterator {
  static const _tokens = <String, String>{
    'chh': 'छ', 'ksh': 'क्ष', 'sh': 'श', 'th': 'थ', 'dh': 'ध', 'ph': 'फ', 'bh': 'भ',
    'kh': 'ख', 'gh': 'घ', 'ch': 'च', 'jh': 'झ', 'aa': 'ा', 'ee': 'ी', 'oo': 'ू',
    'ai': 'ै', 'au': 'ौ', 'ri': 'ृ',
    'a': 'अ', 'i': 'इ', 'u': 'उ', 'e': 'ए', 'o': 'ओ',
    'k': 'क', 'g': 'ग', 'c': 'क', 'j': 'ज', 't': 'त', 'd': 'द', 'n': 'न',
    'p': 'प', 'b': 'ब', 'm': 'म', 'y': 'य', 'r': 'र', 'l': 'ल', 'v': 'व',
    'w': 'व', 's': 'स', 'h': 'ह', 'f': 'फ', 'q': 'क', 'x': 'क्स', 'z': 'ज़',
  };

  static String transliterate(String input) {
    if (input.trim().isEmpty || RegExp(r'[\u0900-\u097F]').hasMatch(input)) return input;
    final words = input.split(RegExp(r'(\s+)'));
    return words.map((word) {
      if (word.trim().isEmpty) return word;
      final lower = word.toLowerCase();
      final out = StringBuffer();
      var i = 0;
      while (i < lower.length) {
        String? value;
        var consumed = 0;
        for (final length in [3, 2, 1]) {
          if (i + length <= lower.length) {
            final token = lower.substring(i, i + length);
            if (_tokens.containsKey(token)) { value = _tokens[token]; consumed = length; break; }
          }
        }
        if (value == null) { out.write(lower[i]); i++; } else { out.write(value); i += consumed; }
      }
      return out.toString();
    }).join();
  }
}

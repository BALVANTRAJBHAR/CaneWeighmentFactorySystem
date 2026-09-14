/// Offline phonetic English-to-Hindi transliteration for operator entry.
/// It produces Devanagari matras instead of concatenating independent vowels.
class HindiTransliterator {
  static const _consonants = <String, String>{
    'chh': 'छ', 'ksh': 'क्ष', 'shr': 'श्र', 'sh': 'श', 'th': 'थ', 'dh': 'ध',
    'ph': 'फ', 'bh': 'भ', 'kh': 'ख', 'gh': 'घ', 'ch': 'च', 'jh': 'झ',
    'ng': 'ङ', 'ny': 'ञ', 'k': 'क', 'g': 'ग', 'c': 'क', 'j': 'ज', 't': 'त',
    'd': 'द', 'n': 'न', 'p': 'प', 'b': 'ब', 'm': 'म', 'y': 'य', 'r': 'र',
    'l': 'ल', 'v': 'व', 'w': 'व', 's': 'स', 'h': 'ह', 'f': 'फ', 'q': 'क',
    'x': 'क्स', 'z': 'ज़',
  };
  static const _independentVowels = <String, String>{
    'aa': 'आ', 'a': 'अ', 'ii': 'ई', 'ee': 'ई', 'i': 'इ', 'uu': 'ऊ', 'oo': 'ऊ',
    'u': 'उ', 'e': 'ए', 'ai': 'ऐ', 'o': 'ओ', 'au': 'औ', 'ri': 'ऋ',
  };
  static const _matras = <String, String>{
    'a': '', 'aa': 'ा', 'i': 'ि', 'ii': 'ी', 'ee': 'ी', 'u': 'ु', 'uu': 'ू',
    'oo': 'ू', 'e': 'े', 'ai': 'ै', 'o': 'ो', 'au': 'ौ', 'ri': 'ृ',
  };

  static String transliterate(String input) {
    if (input.isEmpty || RegExp(r'[\u0900-\u097F]').hasMatch(input)) return input;
    return input.splitMapJoin(RegExp(r'\s+'), onMatch: (m) => m.group(0)!, onNonMatch: _word);
  }

  static String _word(String word) {
    final lower = word.toLowerCase();
    final out = StringBuffer();
    var i = 0;
    while (i < lower.length) {
      final vowel = _longest(lower, i, _independentVowels);
      if (vowel != null) { out.write(_independentVowels[vowel]!); i += vowel.length; continue; }
      final consonant = _longest(lower, i, _consonants);
      if (consonant == null) { out.write(lower[i]); i++; continue; }
      out.write(_consonants[consonant]!);
      i += consonant.length;
      final nextVowel = _longest(lower, i, _matras);
      if (nextVowel != null) {
        final longA = nextVowel == 'a' && _isLongA(lower, i + nextVowel.length);
        out.write(longA ? 'ा' : _matras[nextVowel]!);
        i += nextVowel.length;
      }
    }
    return out.toString();
  }

  static String? _longest(String value, int offset, Map<String, String> map) {
    for (final length in [3, 2, 1]) {
      if (offset + length <= value.length) {
        final token = value.substring(offset, offset + length);
        if (map.containsKey(token)) return token;
      }
    }
    return null;
  }

  static bool _isLongA(String value, int offset) {
    final following = _longest(value, offset, _consonants);
    if (following == null) return false;
    final afterFollowing = offset + following.length;
    return afterFollowing >= value.length ||
        _longest(value, afterFollowing, _consonants) != null;
  }
}

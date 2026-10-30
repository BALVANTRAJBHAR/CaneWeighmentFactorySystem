import 'package:affllp/screens/users/users_screen.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('New User mobile validation', () {
    test('requires a mobile number before the dialog can submit', () {
      expect(validateUserMobile(null), 'Mobile number is required');
      expect(validateUserMobile(''), 'Mobile number is required');
      expect(validateUserMobile('   '), 'Mobile number is required');
    });

    test('requires exactly ten digits', () {
      expect(validateUserMobile('987654321'),
          'Mobile number must be exactly 10 digits');
      expect(validateUserMobile('9876543210'), isNull);
    });
  });
}

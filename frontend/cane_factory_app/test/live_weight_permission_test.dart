import 'package:affllp/providers/live_weight_provider.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('SignalR reading preserves requester-specific save restriction', () {
    final polled = LiveWeight.fromJson({
      'weightKg': 1000,
      'canSaveWeighment': false,
      'weightSaveRestrictedToBridgeHost': true,
    });

    final broadcast = LiveWeight.fromJson({
      'weightKg': 1200,
      'stable': true,
      'deviceConnected': true,
    }, previous: polled);

    expect(broadcast.weightKg, 1200);
    expect(broadcast.canSaveWeighment, isFalse);
    expect(broadcast.weightSaveRestrictedToBridgeHost, isTrue);
  });

  test('SignalR frame cannot clear local-only transport mode', () {
    final local = LiveWeight.fromJson({
      'liveWeightOnlyOnDigitizerPc': true,
      'canSaveWeighment': true,
    });

    final broadcast = LiveWeight.fromJson({
      'weightKg': 500,
      'deviceConnected': true,
    }, previous: local);

    expect(broadcast.liveWeightOnlyOnDigitizerPc, isTrue);
  });

  test('initial state fails closed until per-PC permission is polled', () {
    expect(LiveWeight().canSaveWeighment, isFalse);
  });

  test('local Scale Bridge reading carries the save-time proof unchanged', () {
    final reading = LiveWeight.fromJson({
      'weightKg': 1234.5,
      'stable': true,
      'isLive': true,
      'canSaveWeighment': true,
      'liveWeightOnlyOnDigitizerPc': true,
      'scaleReadingCapturedAtUtc': '2026-10-18T10:20:30.000Z',
      'scaleBridgeSessionId': '11111111-1111-1111-1111-111111111111',
      'scaleBridgeZeroCounter': 7,
      'scaleReadingNonce': 'ABCDEF123456',
      'scaleReadingSignature': 'SIGNED',
    });

    expect(reading.liveWeightOnlyOnDigitizerPc, isTrue);
    expect(reading.saveProof['scaleReadingKg'], 1234.5);
    expect(reading.saveProof['scaleReadingStable'], isTrue);
    expect(reading.saveProof['scaleBridgeZeroCounter'], 7);
    expect(reading.saveProof['scaleReadingSignature'], 'SIGNED');
  });
}

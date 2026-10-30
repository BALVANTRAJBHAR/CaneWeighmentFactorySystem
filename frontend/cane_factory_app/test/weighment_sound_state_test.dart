import 'package:affllp/core/weighment_sound_state.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('zero stays on the platform prompt when configured minimum is zero', () {
    final zero = decideLiveWeightSoundState(
      currentState: SoundState.noVehicle,
      zeroSamples: 0,
      weightQuintal: 0,
      minimumWeightQuintal: 0,
      readingValid: true,
    );
    expect(zero.state, SoundState.noVehicle);
    final vehicle = decideLiveWeightSoundState(
      currentState: zero.state,
      zeroSamples: zero.zeroSamples,
      weightQuintal: 0.01,
      minimumWeightQuintal: 0,
      readingValid: true,
    );
    expect(vehicle.state, SoundState.weighingInProgress);
  });

  test('minimum crossing stays latched through lower non-zero readings', () {
    final reached = decideLiveWeightSoundState(
      currentState: SoundState.belowMinimum,
      zeroSamples: 0,
      weightQuintal: 10,
      minimumWeightQuintal: 10,
      readingValid: true,
    );
    expect(reached.state, SoundState.weighingInProgress);

    final fluctuated = decideLiveWeightSoundState(
      currentState: reached.state,
      zeroSamples: reached.zeroSamples,
      weightQuintal: 9.4,
      minimumWeightQuintal: 10,
      readingValid: true,
    );
    expect(fluctuated.state, SoundState.weighingInProgress);
    expect(fluctuated.zeroSamples, 0);
  });

  test('one zero glitch cannot clear active weighing announcement', () {
    final decision = decideLiveWeightSoundState(
      currentState: SoundState.weighingInProgress,
      zeroSamples: 0,
      weightQuintal: 0,
      minimumWeightQuintal: 10,
      readingValid: true,
    );
    expect(decision.state, SoundState.weighingInProgress);
    expect(decision.zeroSamples, 1);
  });

  test('two valid zero readings clear an unsaved weighing latch', () {
    final decision = decideLiveWeightSoundState(
      currentState: SoundState.weighingInProgress,
      zeroSamples: 1,
      weightQuintal: 0,
      minimumWeightQuintal: 10,
      readingValid: true,
    );
    expect(decision.state, SoundState.noVehicle);
    expect(decision.zeroSamples, 0);
  });

  test('invalid zero reading cannot change the latched state', () {
    final decision = decideLiveWeightSoundState(
      currentState: SoundState.weighingInProgress,
      zeroSamples: 0,
      weightQuintal: 0,
      minimumWeightQuintal: 10,
      readingValid: false,
    );
    expect(decision.state, SoundState.weighingInProgress);
    expect(decision.zeroSamples, 0);
  });
}

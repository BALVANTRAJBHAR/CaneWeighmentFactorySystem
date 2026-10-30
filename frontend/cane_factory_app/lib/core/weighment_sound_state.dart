enum SoundState {
  noVehicle,
  belowMinimum,
  aboveMinimumWaiting,
  weighingInProgress,
  completed
}

class SoundWeightDecision {
  const SoundWeightDecision(this.state, this.zeroSamples);

  final SoundState state;
  final int zeroSamples;
}

/// Resolves the live-weight portion of the sound state machine.
///
/// Once the configured minimum has been reached, normal weight fluctuation must
/// not send the operator back to the vehicle-entry instruction. The weighing
/// state remains latched until save, or until two consecutive valid zero
/// readings prove that an unsaved vehicle actually left the platform.
SoundWeightDecision decideLiveWeightSoundState({
  required SoundState currentState,
  required int zeroSamples,
  required double weightQuintal,
  required double minimumWeightQuintal,
  required bool readingValid,
}) {
  if (!readingValid || currentState == SoundState.completed) {
    return SoundWeightDecision(currentState, zeroSamples);
  }

  if (currentState == SoundState.weighingInProgress) {
    if (weightQuintal <= 0) {
      final nextZeroSamples = zeroSamples + 1;
      return nextZeroSamples >= 2
          ? const SoundWeightDecision(SoundState.noVehicle, 0)
          : SoundWeightDecision(currentState, nextZeroSamples);
    }
    return SoundWeightDecision(currentState, 0);
  }

  if (weightQuintal <= 0) {
    return const SoundWeightDecision(SoundState.noVehicle, 0);
  }
  if (weightQuintal >= minimumWeightQuintal) {
    return const SoundWeightDecision(SoundState.weighingInProgress, 0);
  }
  return const SoundWeightDecision(SoundState.belowMinimum, 0);
}

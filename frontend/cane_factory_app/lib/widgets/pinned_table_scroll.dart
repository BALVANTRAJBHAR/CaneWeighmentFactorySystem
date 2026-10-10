import 'package:flutter/material.dart';

/// A two-axis table viewport whose horizontal scrollbar remains pinned to the
/// bottom of the visible viewport while rows scroll vertically behind it.
///
/// Putting the horizontal scroll view outside the vertical one is important:
/// when it is nested inside the vertical content, its scrollbar is only
/// reachable after scrolling to the final row.
class PinnedTableScroll extends StatelessWidget {
  final ScrollController horizontalController;
  final ScrollController verticalController;
  final Widget child;
  final double minTableWidth;

  const PinnedTableScroll({
    super.key,
    required this.horizontalController,
    required this.verticalController,
    required this.child,
    this.minTableWidth = 0,
  });

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(builder: (context, constraints) {
      final width = minTableWidth > constraints.maxWidth
          ? minTableWidth
          : constraints.maxWidth;
      return Scrollbar(
        controller: horizontalController,
        thumbVisibility: true,
        trackVisibility: true,
        interactive: true,
        thickness: 10,
        scrollbarOrientation: ScrollbarOrientation.bottom,
        child: SingleChildScrollView(
          controller: horizontalController,
          scrollDirection: Axis.horizontal,
          child: ConstrainedBox(
            constraints: BoxConstraints(minWidth: width),
            child: SizedBox(
              height: constraints.maxHeight,
              child: Padding(
                // Keep the last row clear of the permanently visible bar.
                padding: const EdgeInsets.only(bottom: 14),
                child: Scrollbar(
                  controller: verticalController,
                  thumbVisibility: true,
                  trackVisibility: true,
                  interactive: true,
                  scrollbarOrientation: ScrollbarOrientation.right,
                  child: SingleChildScrollView(
                    controller: verticalController,
                    scrollDirection: Axis.vertical,
                    child: child,
                  ),
                ),
              ),
            ),
          ),
        ),
      );
    });
  }
}

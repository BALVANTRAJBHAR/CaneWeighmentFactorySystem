import 'package:cane_factory_app/providers/auth_provider.dart';
import 'package:cane_factory_app/screens/weighment/weighment_corrections_screen.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

Map<String, dynamic> lookup() => {
      'record': {
        'id': 12,
        'name': 'Test farmer',
        'growerCode': '101/1',
        'vehicleNumber': 'UP32AA1111',
        'vehicleTypeId': 1,
        'varietyTypeId': 1,
        'varietyId': 1,
        'rate': 300.0,
        'amount': 3075.0,
        'finalWeightQuintal': 10.25,
        'grossWeightQuintal': 25.25,
        'tareWeightQuintal': 15,
        'status': 'TARE_DONE',
        'revision': 'original',
        'rateDate': '2026-09-10',
      },
      'vehicleTypes': [
        {'id': 1, 'name': 'Truck'}
      ],
      'varietyTypes': [
        {'id': 1, 'name': 'Normal'},
        {'id': 2, 'name': 'Early'}
      ],
      'varieties': [
        {'id': 1, 'name': 'Normal cane', 'varietyTypeId': 1},
        {'id': 2, 'name': 'Early cane', 'varietyTypeId': 2}
      ],
    };

class FakeApi {
  final dio = Dio();
  final calls = <RequestOptions>[];
  bool paid = false, stale = false;
  FakeApi() {
    dio.interceptors.add(InterceptorsWrapper(onRequest: (request, handler) {
      calls.add(request);
      final conflict = paid || (stale && request.method == 'PUT');
      final data = conflict
          ? {
              'code': paid ? 'PAYMENT_COMPLETED' : 'STALE_RECORD',
              'message': paid
                  ? 'Payment already completed. It cannot be edited.'
                  : 'Record changed. Search again.',
            }
          : request.method == 'GET'
              ? lookup()
              : {
                  'vehicleNumber': 'UP32AA1111',
                  'rate': 325.5,
                  'amount': 3336.38,
                  'revision': 'updated',
                  'updatedAt': '2026-09-20T12:00:00Z',
                  'updatedBy': 7,
                  'updatedByName': 'admin-test',
                  'message': request.method == 'PUT'
                      ? 'Purchase corrected successfully.'
                      : 'Calculation ready.',
                };
      handler.resolve(Response(
          requestOptions: request,
          statusCode: conflict ? 409 : 200,
          data: data));
    }));
  }
}

Future<void> open(WidgetTester tester, FakeApi api,
    {String role = 'Admin', Size size = const Size(1200, 1100)}) async {
  await tester.binding.setSurfaceSize(size);
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final auth = AuthProvider()
    ..user = {
      'roles': [role],
      'permissions': ['Purchase.Edit']
    };
  await tester.pumpWidget(ChangeNotifierProvider.value(
      value: auth,
      child: MaterialApp(
          home: Scaffold(body: WeighmentCorrectionsScreen(api: api.dio)))));
  await tester.pumpAndSettle();
}

Future<void> search(WidgetTester tester) async {
  await tester.enterText(find.byType(TextField).first, '12');
  await tester.tap(find.text('Search'));
  await settleOrDialog(tester);
}

// A modal may intentionally keep the underlying form busy until the user answers.
Future<void> settleOrDialog(WidgetTester tester) async {
  for (var i = 0; i < 30; i++) {
    await tester.pump(const Duration(milliseconds: 100));
    if (find.byType(AlertDialog).evaluate().isNotEmpty) {
      await tester.pump(const Duration(milliseconds: 300));
      return;
    }
    if (find.byType(LinearProgressIndicator).evaluate().isEmpty) {
      await tester.pumpAndSettle();
      return;
    }
  }
  fail('Request did not complete or show a dialog');
}

Future<void> preview(WidgetTester tester) async {
  await tester.ensureVisible(find.text('Calculate / Preview'));
  await tester.tap(find.text('Calculate / Preview'));
  await tester.pumpAndSettle();
}

Future<void> save(WidgetTester tester) async {
  await tester.tap(find.text('Update'));
  await settleOrDialog(tester);
  await tester.tap(find.text('Confirm Update'));
  await settleOrDialog(tester);
}

void main() {
  testWidgets('Operator cannot see correction controls despite Purchase.Edit',
      (tester) async {
    final api = FakeApi();
    await open(tester, api, role: 'Operator');
    expect(find.text('Only Admin and Developer can correct weighments.'),
        findsOneWidget);
    expect(find.text('Search'), findsNothing);
    expect(api.calls, isEmpty);
  });

  testWidgets('Admin previews, confirms and sees updated user/time',
      (tester) async {
    final api = FakeApi();
    await open(tester, api);
    await search(tester);
    expect(
        tester
            .widget<FilledButton>(find.widgetWithText(FilledButton, 'Update'))
            .onPressed,
        isNull);
    await preview(tester);
    expect(find.textContaining('3336.38'), findsOneWidget);
    await save(tester);
    final put = api.calls.singleWhere((r) => r.method == 'PUT');
    expect(put.path, '/api/weighment-corrections/purchase/12');
    expect(put.data['revision'], 'original');
    expect(put.data['expectedRate'], 325.5);
    expect(find.text('Purchase corrected successfully.'), findsOneWidget);
    expect(find.text('Last Updated By'), findsOneWidget);
    expect(find.text('admin-test'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Paid on lookup clears editor and explains rejection',
      (tester) async {
    final api = FakeApi()..paid = true;
    await open(tester, api);
    await search(tester);
    expect(find.byType(AlertDialog), findsOneWidget);
    expect(find.text('Payment already completed'), findsOneWidget);
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();
    expect(find.text('Update'), findsNothing);
  });

  testWidgets('Payment after preview still blocks save and exits editor',
      (tester) async {
    final api = FakeApi();
    await open(tester, api);
    await search(tester);
    await preview(tester);
    api.paid = true;
    await save(tester);
    expect(find.text('Payment already completed'), findsOneWidget);
    expect(find.text('Update'), findsNothing);
  });

  testWidgets('Stale correction forces fresh search', (tester) async {
    final api = FakeApi()..stale = true;
    await open(tester, api);
    await search(tester);
    await preview(tester);
    await save(tester);
    expect(find.text('Record changed. Search again.'), findsOneWidget);
    expect(find.text('Update'), findsNothing);
  });

  testWidgets('Changing variety type clears old variety and preview',
      (tester) async {
    final api = FakeApi();
    await open(tester, api);
    await search(tester);
    await preview(tester);
    await tester.tap(find.byType(DropdownButtonFormField<int>).at(1));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Early').last);
    await tester.pumpAndSettle();
    expect(find.text('Normal cane'), findsNothing);
    expect(
        tester
            .widget<FilledButton>(find.widgetWithText(FilledButton, 'Update'))
            .onPressed,
        isNull);
    await preview(tester);
    expect(
        find.text('Select all required fields and enter the vehicle number.'),
        findsOneWidget);
  });

  testWidgets(
      'Developer sale form has vehicle fields only and fits narrow window',
      (tester) async {
    final api = FakeApi();
    await open(tester, api, role: 'Developer', size: const Size(420, 900));
    await tester.tap(find.text('Sale / Purchase'));
    await tester.pumpAndSettle();
    await search(tester);
    expect(api.calls.last.path, '/api/weighment-corrections/sale/12');
    expect(find.textContaining('no linked payment status'), findsOneWidget);
    await tester.scrollUntilVisible(
        find.byType(DropdownButtonFormField<int>), 200,
        scrollable: find.byType(Scrollable).first);
    expect(find.byType(DropdownButtonFormField<int>), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}

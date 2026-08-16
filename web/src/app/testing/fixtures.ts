/**
 * Fixtures for the component specs.
 *
 * The shapes mirror `FreightDispatch.Api/Contracts.cs`, and the values are the same
 * invented load the .NET tests walk — `samples/204-dry-van-2-stop.edi`, LD10041872 — so a
 * spec that asserts on a rendered reference number is asserting on the same data a
 * dispatcher would see on the board.
 */

import {
  AcknowledgmentDto,
  InvoiceDto,
  LoadDetail,
  LoadSummary,
  StatusEventDto,
} from '../api/models';

/** The ISA of `samples/204-flatbed-pipe-delimited.edi`: 105 characters, terminated by a newline. */
export const PIPE_ISA =
  'ISA|00|          |00|          |ZZ|DEMOBROKER     |ZZ|DEMOCARRIER    |260817|1330|!|00501|000004419|0|T|>';

/**
 * A pipe-delimited, newline-terminated interchange. Every delimiter in it is legal and
 * declared in the ISA, and none of them is the one a parser written against the
 * conventional samples would assume.
 */
export const PIPE_EDI =
  [
    PIPE_ISA,
    'GS|SM|DEMOBROKER|DEMOCARRIER|20260817|1330|4419|X|005010',
    'ST|204|0001',
    'B2||TEST||LD10042311||PP',
    'S5|1|CL|46800|L|12|PC',
    'N1|SH|RIDGELINE STEEL PRODUCTS|93|RSP-GRY',
    'S5|2|CU|46800|L|12|PC',
    'N1|CN|GULF COAST FABRICATORS|93|GCF-HOU',
    'SE|6|0001',
    'GE|1|4419',
    'IEA|1|000004419',
  ].join('\n') + '\n';

/** The same shape of file with the conventional `* : ~ ^` delimiters. */
export const STAR_EDI =
  [
    'ISA*00*          *00*          *ZZ*DEMOCARRIER    *ZZ*DEMOBROKER     *260820*1605*^*00501*000004005*0*T*:~',
    'GS*QM*DEMOCARRIER*DEMOBROKER*20260820*1605*4005*X*005010~',
    'ST*214*4005~',
    'B10*LD10041872*LD10041872*DEMO~',
    'LX*1~',
    'AT7*AF*NS***20260818*0715*LT~',
    'MS1*JOLIET*IL*US~',
    'SE*14*4005~',
    'GE*1*4005~',
    'IEA*1*000004005~',
  ].join('\n') + '\n';

export function loadSummary(overrides: Partial<LoadSummary> = {}): LoadSummary {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    shipmentId: 'LD10041872',
    scac: 'DEMO',
    status: 'InTransit',
    statusLabel: 'In transit',
    statusOrder: 4,
    nextStatus: 'AtConsignee',
    nextStatusLabel: 'At consignee',
    equipmentCode: 'TF',
    equipmentLabel: 'Dry van',
    equipmentLength: '53',
    temperatureControl: '',
    trailerNumber: '',
    totalWeight: 42150,
    stopCount: 2,
    extraStops: 0,
    isMultiStop: false,
    currentStopSequence: 2,
    currentStopOrdinal: 2,
    currentStopName: 'BLUE PRAIRIE GROCERS DC 12',
    currentStopCityState: 'MEMPHIS, TN',
    currentStopReason: 'CU',
    currentStopIsPickup: false,
    stopProgress: '2/2',
    nextActionLabel: 'At consignee',
    originName: 'NORTHWIND FOODS PROCESSING',
    originCityState: 'JOLIET, IL',
    originEarliest: '2026-08-18T07:00',
    originLatest: '2026-08-18T12:00',
    destinationName: 'BLUE PRAIRIE GROCERS DC 12',
    destinationCityState: 'MEMPHIS, TN',
    destinationEarliest: '2026-08-19T06:00',
    destinationLatest: '2026-08-19T14:00',
    primaryReference: 'LD10041872',
    billOfLading: 'BOL8842190',
    isProduction: false,
    hasTenderDiagnostics: false,
    acknowledgmentVerdict: 'A',
    acknowledgmentLabel: 'Accepted',
    tenderRejected: false,
    hasInvoice: false,
    invoiceNumber: '',
    invoiceTotal: 0,
    eventCount: 4,
    receivedAt: '2026-08-18T05:00:00',
    ...overrides,
  };
}

export function statusEvent(overrides: Partial<StatusEventDto> = {}): StatusEventDto {
  return {
    id: '22222222-2222-2222-2222-222222222222',
    status: 'InTransit',
    statusLabel: 'In transit',
    statusOrder: 4,
    stopSequence: 1,
    stopOrdinal: 1,
    stopName: 'NORTHWIND FOODS PROCESSING',
    statusCode: 'AF',
    statusCodeName: 'Carrier Departed Pickup Location with Shipment',
    reasonCode: 'NS',
    occurredAt: '2026-08-18T07:15:00',
    timeCode: 'LT',
    city: 'JOLIET',
    state: 'IL',
    cityState: 'JOLIET, IL',
    note: '',
    recordedAt: '2026-08-20T16:05:00',
    edi214: STAR_EDI,
    interchangeControlNumber: '000004005',
    transactionControlNumber: '4005',
    roundTripDiagnostics: [],
    roundTripClean: true,
    ...overrides,
  };
}

export function acknowledgment(overrides: Partial<AcknowledgmentDto> = {}): AcknowledgmentDto {
  return {
    id: '33333333-3333-3333-3333-333333333333',
    verdict: 'A',
    verdictLabel: 'Accepted',
    transactionAcknowledgmentCode: 'A',
    transactionAcknowledgmentLabel: 'Accepted',
    rejected: false,
    acknowledgedInterchangeControlNumber: '000004417',
    interchangeControlNumber: '000004001',
    transactionControlNumber: '4001',
    findings: [],
    outOfScope: [],
    roundTripDiagnostics: [],
    roundTripClean: true,
    generatedAt: '2026-08-20T16:05:00',
    edi: 'ISA*00*          *00*          *ZZ*DEMOCARRIER    *ZZ*DEMOBROKER     *260820*1605*^*00501*000004001*0*T*:~\nST*997*4001~\nAK1*SM*4417*005010~\nAK5*A~\nSE*6*4001~\n',
    ...overrides,
  };
}

export function invoice(overrides: Partial<InvoiceDto> = {}): InvoiceDto {
  return {
    id: '44444444-4444-4444-4444-444444444444',
    invoiceNumber: 'INV-LD10041872',
    invoiceDate: '2026-08-20T00:00:00',
    shippedOn: '2026-08-18T07:15:00',
    deliveredOn: '2026-08-19T13:05:00',
    charges: [
      {
        lineNumber: 1,
        description: 'LINEHAUL',
        specialChargeCode: 'LHS',
        amount: 2653.75,
        amountCents: 265375,
        rate: 2.5,
        rateQualifier: 'CW',
        weight: 42150,
        quantity: 24,
      },
      {
        lineNumber: 2,
        description: 'FUEL SURCHARGE 22 PERCENT OF LINEHAUL',
        specialChargeCode: '405',
        amount: 583.83,
        amountCents: 58383,
        rate: null,
        rateQualifier: '',
        weight: null,
        quantity: null,
      },
    ],
    total: 3237.58,
    totalCents: 323758,
    totalWeight: 42150,
    totalQuantity: 24,
    currencyCode: 'USD',
    paymentTermsDays: 30,
    interchangeControlNumber: '000004008',
    transactionControlNumber: '4008',
    roundTripDiagnostics: [],
    roundTripClean: true,
    edi: 'ISA*00*          *00*          *ZZ*DEMOCARRIER    *ZZ*DEMOBROKER     *260820*1605*^*00501*000004008*0*T*:~\nST*210*4008~\nB3**INV-LD10041872*LD10041872*PP*L*20260820*323758****DEMO~\nL1*1*2.5*CW*265375****LHS****LINEHAUL~\nSE*23*4008~\n',
    ...overrides,
  };
}

export function loadDetail(overrides: Partial<LoadDetail> = {}): LoadDetail {
  return {
    summary: loadSummary(),
    purposeCode: '00',
    paymentMethod: 'PP',
    paymentMethodLabel: 'Prepaid',
    tenderedBy: 'DEMOBROKER',
    tenderedTo: 'DEMOCARRIER',
    billTo: null,
    references: [
      { value: 'LD10041872', qualifier: 'OQ', qualifierName: 'Order number' },
      { value: 'BOL8842190', qualifier: 'BM', qualifierName: 'Bill of lading' },
    ],
    notes: [],
    stops: [],
    events: [statusEvent()],
    tenderDiagnostics: [],
    acknowledgment: acknowledgment(),
    invoice: null,
    sourceEdi: STAR_EDI,
    ...overrides,
  };
}

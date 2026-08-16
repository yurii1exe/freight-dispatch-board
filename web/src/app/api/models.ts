/** Mirrors the records in FreightDispatch.Api/Contracts.cs. */

export type StatusKey =
  | 'Tendered'
  | 'Dispatched'
  | 'AtShipper'
  | 'Loaded'
  | 'InTransit'
  | 'AtConsignee'
  | 'Delivered';

export interface LoadSummary {
  id: string;
  shipmentId: string;
  scac: string;
  status: StatusKey;
  statusLabel: string;
  statusOrder: number;
  nextStatus: StatusKey | null;
  nextStatusLabel: string | null;
  equipmentCode: string;
  equipmentLabel: string;
  equipmentLength: string;
  temperatureControl: string;
  trailerNumber: string;
  totalWeight: number | null;
  stopCount: number;
  extraStops: number;
  isMultiStop: boolean;
  currentStopSequence: number;
  currentStopOrdinal: number;
  currentStopName: string;
  currentStopCityState: string;
  currentStopReason: string;
  currentStopIsPickup: boolean;
  stopProgress: string;
  nextActionLabel: string;
  originName: string;
  originCityState: string;
  originEarliest: string | null;
  originLatest: string | null;
  destinationName: string;
  destinationCityState: string;
  destinationEarliest: string | null;
  destinationLatest: string | null;
  primaryReference: string;
  billOfLading: string;
  isProduction: boolean;
  hasTenderDiagnostics: boolean;
  /** AK901 of the 997 that answered this load's interchange: A, E, P or R. */
  acknowledgmentVerdict: string;
  acknowledgmentLabel: string;
  /** True when the 997 told the partner this particular tender was refused. */
  tenderRejected: boolean;
  hasInvoice: boolean;
  invoiceNumber: string;
  invoiceTotal: number;
  eventCount: number;
  receivedAt: string;
}

export interface PartyDto {
  entityIdentifierCode: string;
  name: string;
  idQualifier: string;
  idCode: string;
  address1: string;
  address2: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  cityState: string;
  contactName: string;
  contactPhone: string;
}

export interface ReferenceDto {
  value: string;
  qualifier: string;
  qualifierName: string;
}

export interface StopDto {
  sequence: number;
  reasonCode: string;
  reasonName: string;
  isPickup: boolean;
  isCurrent: boolean;
  isComplete: boolean;
  location: PartyDto;
  earliest: string | null;
  latest: string | null;
  timeCode: string;
  isAppointment: boolean;
  weight: number | null;
  weightUnit: string;
  units: number | null;
  unitOfMeasure: string;
  references: ReferenceDto[];
  notes: string[];
  commodities: string[];
}

export interface StatusEventDto {
  id: string;
  status: StatusKey;
  statusLabel: string;
  statusOrder: number;
  stopSequence: number;
  stopOrdinal: number;
  stopName: string;
  statusCode: string;
  statusCodeName: string;
  reasonCode: string;
  occurredAt: string;
  timeCode: string;
  city: string;
  state: string;
  cityState: string;
  note: string;
  recordedAt: string;
  edi214: string;
  interchangeControlNumber: string;
  transactionControlNumber: string;
  roundTripDiagnostics: string[];
  roundTripClean: boolean;
}

/** The 997 that went back for the interchange a load arrived in. */
export interface AcknowledgmentDto {
  id: string;
  /** AK901 across every group: A, E, P or R. */
  verdict: string;
  verdictLabel: string;
  /** AK501 of this load's own transaction set. */
  transactionAcknowledgmentCode: string;
  transactionAcknowledgmentLabel: string;
  rejected: boolean;
  acknowledgedInterchangeControlNumber: string;
  interchangeControlNumber: string;
  transactionControlNumber: string;
  /** Every element 716 and 718 error, already expanded into a sentence. */
  findings: string[];
  /** Interchange-level problems a 997 structurally cannot report. A TA1 does. */
  outOfScope: string[];
  roundTripDiagnostics: string[];
  roundTripClean: boolean;
  generatedAt: string;
  edi: string;
}

/** One charge line on the 210. */
export interface InvoiceChargeDto {
  lineNumber: number;
  description: string;
  /** L108, element 150: LHS linehaul, SOC stop-off, 405 fuel surcharge. */
  specialChargeCode: string;
  amount: number;
  /** L104 as it goes on the wire: N2, so cents with no decimal point. */
  amountCents: number;
  rate: number | null;
  rateQualifier: string;
  weight: number | null;
  quantity: number | null;
}

/** The 210 raised on delivery. */
export interface InvoiceDto {
  id: string;
  invoiceNumber: string;
  invoiceDate: string;
  shippedOn: string | null;
  deliveredOn: string | null;
  charges: InvoiceChargeDto[];
  total: number;
  totalCents: number;
  totalWeight: number | null;
  totalQuantity: number | null;
  currencyCode: string;
  paymentTermsDays: number;
  interchangeControlNumber: string;
  transactionControlNumber: string;
  roundTripDiagnostics: string[];
  roundTripClean: boolean;
  edi: string;
}

export interface LoadDetail {
  summary: LoadSummary;
  purposeCode: string;
  paymentMethod: string;
  paymentMethodLabel: string;
  tenderedBy: string;
  tenderedTo: string;
  billTo: PartyDto | null;
  references: ReferenceDto[];
  notes: string[];
  stops: StopDto[];
  events: StatusEventDto[];
  tenderDiagnostics: string[];
  acknowledgment: AcknowledgmentDto | null;
  invoice: InvoiceDto | null;
  sourceEdi: string;
}

export interface TenderResult {
  loads: LoadSummary[];
  diagnostics: string[];
  segmentCount: number;
  delimiters: string;
  acknowledgment: AcknowledgmentDto | null;
  explanation: string;
}

export interface SampleTender {
  name: string;
  title: string;
  description: string;
  edi: string;
}

export interface StatusOption {
  key: StatusKey;
  label: string;
  order: number;
  statusCode: string;
  statusCodeName: string;
}

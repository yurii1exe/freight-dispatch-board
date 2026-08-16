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
  sourceEdi: string;
}

export interface TenderResult {
  loads: LoadSummary[];
  diagnostics: string[];
  segmentCount: number;
  delimiters: string;
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

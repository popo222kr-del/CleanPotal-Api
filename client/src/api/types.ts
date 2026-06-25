// API DTO와 매칭되는 타입 (camelCase JSON)

export interface UserDto {
  id: number;
  username: string;
  realName: string;
  teamName: string;
  jobTitle: string;
  isAdmin: boolean;
  canManageFiles: boolean;
  canManageSchedule: boolean;
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  user: UserDto;
}

// ── 근무표 ──
export interface RosterDayHeader {
  day: number;
  dayOfWeek: string;
  isWeekend: boolean;
  isHoliday: boolean;
}
export interface RosterCell {
  date: string;
  shiftType: string;
  isPredicted: boolean;
}
export interface RosterMember {
  name: string;
  jobTitle: string;
  cells: RosterCell[];
  totalWorkDays: number;
}
export interface RosterTeam {
  team: string;
  members: RosterMember[];
  dailyCounts: number[];
  grandTotal: number;
}
export interface RosterMonth {
  year: number;
  month: number;
  days: RosterDayHeader[];
  teams: RosterTeam[];
}
export interface StampedCell {
  name: string;
  date: string;
  shiftType: string;
}

// ── 인수인계 ──
export interface Handover {
  id: number;
  vendor: string;
  category: string;
  owner: string;
  content: string;
  inDate: string | null;
  outDate: string | null;
  status: string;
  deliveryMethod: string;
  memo: string;
  progressPercent: number;
  creatorName: string;
  createDate: string;
  modifierName: string;
  modifyDate: string | null;
}

// ── 포탈 ──
export interface PortalItem {
  id: number;
  groupId: number;
  title: string;
  path: string;
  type: string;
  sortOrder: number;
}
export interface PortalGroup {
  id: number;
  name: string;
  sortOrder: number;
  items: PortalItem[];
}

// ── 사용자 관리 (전체 필드) ──
export interface UserFull {
  id: number;
  username: string;
  realName: string;
  teamName: string;
  jobTitle: string;
  email: string;
  phoneNumber: string;
  employeeNumber: string;
  hireDate: string;
  isResigned: boolean;
  resignDate: string;
  isAdmin: boolean;
  canManageFiles: boolean;
  canManageNotices: boolean;
  canManageVendors: boolean;
  canManageSchedule: boolean;
  canAccessEtcMenu: boolean;
}

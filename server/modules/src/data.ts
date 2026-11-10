// Типы и загрузка общих данных из shared/data.
// В образе Nakama файлы лежат в /nakama/data/modules/data/ (см. server/Dockerfile).
// Глобального состояния между запросами в JS-рантайме Nakama нет,
// поэтому данные читаются там, где нужны.
// Описание полей — shared/data/README.md; C#-двойник — client/Assets/_Game/Scripts/Fishing/Data/FishData.cs.

interface IntRange {
  min: number;
  max: number;
}

interface WeightRange {
  min: number;
  typical: number;
  qualifying: number;
  trophy: number;
  max: number;
}

type FightStyle = 'steady' | 'jerky' | 'heavy' | 'deep' | 'jumps';

/** Наживка вида: id из tackle.json и вкус 1–100 (доля от полного клёва, %). */
interface BaitTaste {
  id: string;
  taste: number;
}

interface FishSpecies {
  id: string;
  weightG: WeightRange;
  trophyChance: number;
  depthCm: IntRange;
  activeHours: number[];
  baits: BaitTaste[];
  biteRatePerMin: number;
  maxLineMm: number;
  strength: number;
  stamina: number;
  fightStyle: FightStyle;
  pricePerKg: number;
}

interface RodData {
  id: string;
  type: 'float' | 'bottom' | 'spinning';
  /** С какого уровня игрока вещь продаётся (нет поля — с первого). */
  minLevel?: number;
  lengthCm: number;
  maxLoadG: number;
  flex: number;
  castRangeCm: number;
  price: number;
}

interface ReelData {
  id: string;
  maxDragG: number;
  retrieveCmPerSec: number;
  fightSpeedCmPerSec: number;
  powerG: number;
  spoolCapacityM: number;
  price: number;
}

interface LineData {
  id: string;
  diameterMm: number;
  breakingLoadG: number;
  price: number;
}

interface BaitData {
  id: string;
  rodType: 'float' | 'spinning';
  /** Род: animal | plant | live у наживки; spinner | spoon | soft | wobbler у приманки. */
  kind: string;
  /** Ход приманки: top | surface | mid | bottom (только у приманок). */
  run?: string;
  /** С какого уровня игрока вещь продаётся (нет поля — с первого). */
  minLevel?: number;
  consumable: boolean;
  price: number;
}

interface TackleFile {
  version: number;
  rods: RodData[];
  reels: ReelData[];
  lines: LineData[];
  baits: BaitData[];
}

interface FishFile {
  version: number;
  species: FishSpecies[];
}

function loadFishData(nk: nkruntime.Nakama): FishSpecies[] {
  const raw = nk.fileRead('data/fish.json');
  const parsed = JSON.parse(raw) as FishFile;
  return parsed.species;
}

/** Место ловли на водоёме: своя глубина и свой набор рыбы (shared/data/locations.json, версия 4). */
interface LakeSpot {
  id: string;
  /** С какого уровня игрока место открыто (1 — сразу). */
  minLevel: number;
  /** Место на воде: нужна лодка. Нет поля — не нужна. */
  needsBoat?: boolean;
  shoreDepthCm: number;
  slopeCmPerM: number;
  maxDepthCm: number;
  fish: string[];
}

interface LakeLocation {
  id: string;
  unlockPrice: number;
  spots: LakeSpot[];
}

interface LocationFile {
  version: number;
  locations: LakeLocation[];
}

/** Лодка: открывает места на воде; покупка насовсем или аренда на время (shared/data/transport.json, версия 1). */
interface BoatData {
  id: string;
  price: number;
  rentPrice: number;
  rentMinutes: number;
}

interface TransportFile {
  version: number;
  boats: BoatData[];
}

/** Награда за уровень (shared/data/levels.json, версия 1): монеты всегда, остальное — на значимых уровнях. */
interface LevelReward {
  level: number;
  coins: number;
  baits?: { id: string; count: number }[];
  lures?: string[];
  tackle?: string[];
  boatMinutes?: number;
}

interface LevelRewardFile {
  version: number;
  levels: LevelReward[];
}

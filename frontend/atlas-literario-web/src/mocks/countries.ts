import type { Continent, CountryDto } from '@/types/api'

/**
 * Subconjunto do seed de países (o backend terá ≈ 250). Suficiente para os mocks:
 * inclui todos os países usados na biblioteca simulada e alguns extras por continente.
 */
type Row = [iso: string, iso2: string, name: string, namePt: string, continent: Continent]

const ROWS: Row[] = [
  ['ARG', 'AR', 'Argentina', 'Argentina', 'SouthAmerica'],
  ['BRA', 'BR', 'Brazil', 'Brasil', 'SouthAmerica'],
  ['CHL', 'CL', 'Chile', 'Chile', 'SouthAmerica'],
  ['COL', 'CO', 'Colombia', 'Colômbia', 'SouthAmerica'],
  ['PER', 'PE', 'Peru', 'Peru', 'SouthAmerica'],
  ['URY', 'UY', 'Uruguay', 'Uruguai', 'SouthAmerica'],
  ['CAN', 'CA', 'Canada', 'Canadá', 'NorthAmerica'],
  ['CUB', 'CU', 'Cuba', 'Cuba', 'NorthAmerica'],
  ['MEX', 'MX', 'Mexico', 'México', 'NorthAmerica'],
  ['USA', 'US', 'United States', 'Estados Unidos', 'NorthAmerica'],
  ['AUT', 'AT', 'Austria', 'Áustria', 'Europe'],
  ['CZE', 'CZ', 'Czechia', 'Tchéquia', 'Europe'],
  ['DEU', 'DE', 'Germany', 'Alemanha', 'Europe'],
  ['ESP', 'ES', 'Spain', 'Espanha', 'Europe'],
  ['FRA', 'FR', 'France', 'França', 'Europe'],
  ['GBR', 'GB', 'United Kingdom', 'Reino Unido', 'Europe'],
  ['GRC', 'GR', 'Greece', 'Grécia', 'Europe'],
  ['IRL', 'IE', 'Ireland', 'Irlanda', 'Europe'],
  ['ITA', 'IT', 'Italy', 'Itália', 'Europe'],
  ['NOR', 'NO', 'Norway', 'Noruega', 'Europe'],
  ['POL', 'PL', 'Poland', 'Polônia', 'Europe'],
  ['PRT', 'PT', 'Portugal', 'Portugal', 'Europe'],
  ['RUS', 'RU', 'Russia', 'Rússia', 'Europe'],
  ['SWE', 'SE', 'Sweden', 'Suécia', 'Europe'],
  ['UKR', 'UA', 'Ukraine', 'Ucrânia', 'Europe'],
  ['XKX', 'XK', 'Kosovo', 'Kosovo', 'Europe'],
  ['AGO', 'AO', 'Angola', 'Angola', 'Africa'],
  ['EGY', 'EG', 'Egypt', 'Egito', 'Africa'],
  ['KEN', 'KE', 'Kenya', 'Quênia', 'Africa'],
  ['MOZ', 'MZ', 'Mozambique', 'Moçambique', 'Africa'],
  ['NGA', 'NG', 'Nigeria', 'Nigéria', 'Africa'],
  ['ZAF', 'ZA', 'South Africa', 'África do Sul', 'Africa'],
  ['CHN', 'CN', 'China', 'China', 'Asia'],
  ['IND', 'IN', 'India', 'Índia', 'Asia'],
  ['IRN', 'IR', 'Iran', 'Irã', 'Asia'],
  ['ISR', 'IL', 'Israel', 'Israel', 'Asia'],
  ['JPN', 'JP', 'Japan', 'Japão', 'Asia'],
  ['KOR', 'KR', 'South Korea', 'Coreia do Sul', 'Asia'],
  ['TUR', 'TR', 'Turkey', 'Turquia', 'Asia'],
  ['AUS', 'AU', 'Australia', 'Austrália', 'Oceania'],
  ['NZL', 'NZ', 'New Zealand', 'Nova Zelândia', 'Oceania'],
  ['ATA', 'AQ', 'Antarctica', 'Antártida', 'Antarctica'],
]

export const MOCK_COUNTRIES: CountryDto[] = /* @__PURE__ */ ROWS.map(
  ([iso, iso2, name, namePt, continent]) => ({
    iso,
    iso2,
    name,
    namePt,
    continent,
    geoJsonId: iso,
  }),
)

/** Total do seed real de países/territórios (usado no progresso do passaporte nos mocks). */
export const MOCK_TOTAL_COUNTRIES = 250

export function countryIndex(
  countries: readonly CountryDto[] = MOCK_COUNTRIES,
): Map<string, CountryDto> {
  return new Map(countries.map((country) => [country.iso, country]))
}

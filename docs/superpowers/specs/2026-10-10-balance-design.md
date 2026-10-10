# Tasapainovaihe 1: vaiennut asema

Käyttäjä 10.10.2026: "tee päätöksiä ja ala toteuttaa". Vaihe 6c-2 teki pelistä vaikeamman. Vihollinen ei enää
rynnäköi suin päin, ja pelaajan hyökkäykset kaatuvat kahteen syyhyn:

- **NoFire:** tuliasema ei näe poteroissa olevia miehiä, joten hyökkäys keskeytyy 20 sekunnissa, vaikka tuliasema
  ampuu aluetulta tunnettuun asemaan.
- **NotSuppressed:** rynnäkkö vaatii, että jokainen *nähty* asemamies on lamautettu. Jos ketään ei nähdä, kukaan ei
  mene. Poterossa olevaa miestä ei näe, joten rynnäkkö ei lähde koskaan.

## Tavoite (iskuosasto, 24 siementä, ehdotus)

| | Suomalaisia poissa | Vihollisia poissa | Johtaja kaatui |
|---|---|---|---|
| nyt (6c-2) | 3,0 | 3,2 | 7/24 |
| tavoite | ≤ 3,5 | ≥ 5 | ≥ 12/24 |

## Päätökset

1. **Kuultu tuli.** `Contact.LastFireTick` kertoo, milloin puoli viimeksi näki tai kuuli miehen ampuvan.
   - Laukaus kuuluu aseen äänen kantaman päähän (`WeaponDef.NoiseRadiusCm`). Jos kukaan puolen miehistä ei ole niin
     lähellä, kuulija ei saa tietoa.
   - Tieto perustuu vain siihen, mitä puoli kuulee tai näkee, ei miehen todelliseen tilaan.
2. **Vaiennut asema.** Rynnäkkö lähtee, kun jokainen tunnettu asemamies on jompaakumpaa:
   - nähty, ja maassa tai lamautettu kuten ennen
   - näkymätön, ja häntä ei ole kuultu ampuvan `SilentTicks` (8 s) aikana, vaikka tuliasema on ampunut asemaa
     vähintään sen ajan.

   Vähintään yhden asemamiehen täytyy olla tunnettu. Sokeaan rynnäkköön (asemasta ei tiedetä mitään) ei edelleenkään
   lähdetä.
3. **NoFire** keskeyttää vain, jos tuliasema ei ole ampunut lainkaan 20 sekunnin aikana. Aluetuli tunnettuun
   asemaan pitää päät alhaalla, koska läheltä menevät luodit lamauttavat näkemättäkin.
4. **Riski:** asema, joka vain pidättää tulta (väijytys), näyttää vaienneelta. Tämä on hyväksytty sodan kitka.
   Kokemus vaikuttaa lamautuksen vaatimukseen kuten ennenkin (`ExperienceTimePct`).

5. **Iskuryhmä odottaa makuulla.** Kokoontumispaikalla iskuryhmä ei nouse ampuma-asentoon nähdäkseen aseman, vaan
   odottaa maassa sen näkymättömissä. Tuliryhmä ottaa ampuma-asennon kuten ennen. Jäljitys näytti, että suomalaiset
   kaatuivat enimmäkseen iskuryhmässä kokoontumispaikalla, noin 40 m päässä asemasta.
6. **Mittari jatkaa reittiä kontaktin jälkeen.** Kun hyökkäystä ei ole käynnissä eikä vihollista ole nähty 20
   sekuntiin, suomalaiset jatkavat reittiä seuraavaan asemaan kuten pelaaja. Ennen mittari jäi paikoilleen
   ensimmäisen vallatun aseman jälkeen, eikä se siksi mitannut koko tehtävää.

## Mittaukset (24 siementä, ehdotus)

| Ajo | Suomalaisia poissa | Vihollisia poissa | Johtaja kaatui | Vastahyökkäyksiä (suomalaisia kaatui niissä) |
|---|---|---|---|---|
| 6c-2, vanha mittari | 3,0 | 3,2 | 7/24 | 1,0 (1,1) |
| vaiennut asema, vanha mittari | 3,6 | 3,1 | 6/24 | 0,8 (1,3) |
| main, uusi mittari (jatkaa reittiä) | 4,4 | 3,4 | 6/24 | 1,3 (2,2) |
| vaiennut asema, uusi mittari | 4,6 | 3,2 | 8/24 | 1,1 (2,1) |
| + iskuryhmä odottaa makuulla | 4,7 | 3,8 | 9/24 | 1,3 (2,0) |
| + suojatulibonus tuliryhmälle (kokeiltu, ei otettu) | 4,5 | 3,0 | 8/24 | 1,2 (1,8) |
| + rynnäkön lamautus 200 (kokeiltu, ei vaikutusta, ei otettu) | 4,7 | 3,8 | 9/24 | 1,3 (2,0) |

**Johtopäätös:**
- Tavoitetta (vihollisia ≥ 5, johtaja ≥ 12/24) ei saavutettu taktiikan keinoin. Vaiennut asema ja makuulla odotus
  jäävät käyttöön: ne korjaavat jumiutumisen poteroiden edessä ja turhan paljastumisen, ja tulos parani hieman
  (vihollisia 3,4 → 3,8, johtaja 6 → 9/24).
- Iskuosasto on skriptatulle mittarille vaikea: seitsemän miestä hyökkää yhdeksää poteroitunutta vastaan, joilla on
  konekivääri. Mittarin "pelaaja" hyökkää aina ensimmäiseen nähtyyn mieheen ehdotuksella. Oikea pelaaja valitsee
  kohteen, taktiikan ja reitin.
- Seuraava askel on käyttäjän koepeli. Tehtävän vaikeus (vihollisten määrä ja asemat) säädetään skenaarioiden
  eepoksessa, jossa tehtäviä tehdään lisää.


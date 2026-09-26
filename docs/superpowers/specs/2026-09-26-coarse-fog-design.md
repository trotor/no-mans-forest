# No Man's Forest – sumu karkeammalla ruudukolla (vaihe 4b)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("sumun päivitystä voi pohtia isommassa mittakaavassa ja laskea tarkasti vain näkymisen line of sightit"), toteutetaan suoraan

## 1. Periaate

Kaksi erillistä asiaa:
- **Havaitseminen:** näkeekö sotilas vihollisen. Tämä on pelin sääntö, ja se lasketaan tarkasti sotilasparien välisellä näkölinjalla metrin soluissa (VisionSystem, ennallaan).
- **Sumu:** pelaajalle näytettävä arvio siitä, minne omat näkevät. Se on pelkkä näyttö eikä vaikuta sääntöihin, joten se lasketaan karkeammin.

## 2. Sumu

- **Ruudukko:** lohkot ovat 4 × 4 m. Kilometrin kartalla lohkoja on 250 × 250.
- **Lohkon tiivistelmä:** tehdään kerran kartan latauksessa:
  - maan korkeus on solujen keskiarvo
  - näköä peittävä kasvusto (yli 170 cm korkeat esteet) on solujen peittävyyden keskiarvo × 4 m
- **Säteet:** jokaiselta omalta toimintakykyiseltä mieheltä lohkoruudukon kehälle 150 m säteelle. Lohko näkyy, jos seisova mies sen maanpinnalla näkyisi. Mäki ja tiheä metsä katkaisevat näkymän.
- **Välimuisti:** miehen näkymä lasketaan uudelleen vain, kun hänen lohkonsa tai silmänkorkeutensa muuttuu (tai hän kaatuu). Muuten käytetään edellistä. Versionumero kasvaa vain, kun näkyvä alue muuttuu.
- **Näyttö:** sumutekstuurissa on yksi tekseli lohkoa kohden ja lineaarinen suodatus, joten reunat ovat pehmeät.
- **Kaatuneet** eivät näe.

## 3. Testaus

- **Avoin kenttä:** alle 150 m näkyy, yli 150 m ei.
- **Harjanne** piilottaa takarinteen.
- **Tiheä metsä** näkyy reunasta, mutta ei syvältä.
- **Vertailu tarkkaan metrin näkyvyysalueeseen:** vanhalla kartalla lohkoista vähintään 85 % samoin.
- **Välimuisti:** paikallaan olevia ei lasketa uudelleen eikä versio muutu. Liikkunut lasketaan.
- **Kilometrin kartta:** 20 päivitystä, joissa kaikki 4 miestä liikkuvat, alle 300 ms.
- **GameSession ja näkymät:** käyttävät uutta sumua.

# No Man's Forest – suunta kohteeseen ja kranaatit poteroissa (vaihe 4l)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("korjaa myös kranaatit poteroihin; lisää toiminto, että jostain näkee suurin piirtein kohdealueen, etenkin kun ollaan palaamassa takaisin"), toteutetaan suoraan
Liittyy: poterot `2026-09-26-foxholes-squad-control-design.md`, tehtävät `2026-09-26-missions-design.md`

## 1. Kranaatit ja poterot

Poterossa oleva mies on suojassa kranaatilta, joka räjähtää poteron ulkopuolella:
- Kyykyssä tai maassa sirpaleet lentävät hänen ylitseen, eli hän on täysin suojassa.
- Seisaaltaan hänen päänsä ja olkapäänsä ovat reunan yllä, joten hän on suojassa puolet kerroista.
- Suojassa oleva mies saa puolet lamautuksesta, kuten ennenkin.
- Samaan soluun eli poteroon osunut kranaatti ei ole suojattu.

## 2. Suunta kohteeseen

**Kohde** on seuraavan keskeneräisen tavoitteen paikka (`ObjectiveGuide.Next`), vain omien tietojen perusteella:
- **Alueelle vieminen** (`reach_zone`): alueen keskikohta, esimerkiksi "Lähtöalue".
- **Esineen ottaminen** (`pick_up`):
  - jos oma mies kantaa esinettä, hänen paikkansa
  - muuten vihollisen johtajan nähty tai viimeksi nähty paikka (käskyn mukaan johtaja kantaa papereita), myös kaatuneena
  - muuten ilmoitettu vihollisen asema
- **Ei kohdetta:** tehtävän päätyttyä tai kun paperi (käsky tai kartta) on auki.

**Näkymä:**
- Kun kohde on ruudun ulkopuolella, ruudun reunalla on keltainen nuoli, jossa lukee nimi ja etäisyys joukkueesta, esimerkiksi "Lähtöalue 320 m".
- Kun kohde näkyy ruudulla, sen yllä on osoitin.
- Kohdealueen ääriviivat piirretään maastoon katkoviivalla nimen kanssa sumun ja latvusten päälle kaikilla zoomitasoilla.

### 2.1 Milloin ohjain näkyy (lisätty 2026-09-26)

Käyttäjän toive: "enemy area voisi hävitä, kun sitä tullaan lähelle tai on liikettä; se voisi palata muistuttamaan suunnasta, jos pelaaja ei tee mitään tai ei tapahdu mitään".

- **Häipyy 0,6 sekunnissa,** kun jokin seuraavista pätee:
  - pelaaja antaa käskyn
  - joku omista liikkuu
  - ammutaan, kranaatti räjähtää tai joku haavoittuu
  - joukkue on kohteen lähellä: alle 60 m pisteestä tai alueen reunasta 30 m sisällä
- **Palaa,** kun on ollut hiljaista 12 s eikä joukkue ole kohteen lähellä.
- **Tehtävän alussa** ohjain näkyy heti.

## 3. Testaus

- **Sim:** poterossa kyykkivä mies ei haavoitu 2 m päässä räjähtävästä kranaatista, avoimella haavoittuu, ja seisova mies on poterossa suojassa osan kerroista.
- **Client:**
  - kohde vaihtuu ilmoitetusta vihollisesta nähtyyn johtajaan ja sieltä lähtöalueelle
  - ohjain häipyy toiminnan ajaksi ja palaa hiljaisuuden jälkeen
  - lähellä se ei palaa
  - se häipyy liukuen
- **Godot:** käännös ja kuvakaappaus.

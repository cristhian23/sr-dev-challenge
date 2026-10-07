
# Pasos para probar el proyecto:

git clone https://github.com/cristhian23/sr-dev-challenge
cd sr-dev-challenge
Copy-Item .env.example .env
docker compose up --build --wait --wait-timeout 240

# Desde la raiz del proyecto!


# Backend: compilación y pruebas unitarias
dotnet build Refidomsa.slnx --configuration Release
dotnet test Refidomsa.slnx --configuration Release --no-build

# Pruebas de Playwright
npm --prefix frontend ci
npm --prefix frontend exec -- playwright install chromium
npm --prefix frontend run test:e2e

nota: tener corriendo el proyecto para esta prueba.


# usarios previamente registrados para pruebas:

Usuario	            Rol	            Contraseña
distribuidor.norte	Distribuidor	PruebaRefidomsa_2026!
distribuidor.sur	Distribuidor	PruebaRefidomsa_2026!
operador	        Operador	    PruebaRefidomsa_2026!


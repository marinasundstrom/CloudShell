FROM golang:1.22 AS build

WORKDIR /src
COPY sdk/go/cloudshell ./sdk/go/cloudshell
COPY samples/GoContainerApp/App ./samples/GoContainerApp/App

WORKDIR /src/samples/GoContainerApp/App
RUN CGO_ENABLED=0 GOOS=linux go build -o /out/go-container-app .

FROM gcr.io/distroless/static-debian12:nonroot

WORKDIR /app
COPY --from=build /out/go-container-app /app/go-container-app
EXPOSE 8080

USER nonroot:nonroot
ENTRYPOINT ["/app/go-container-app"]

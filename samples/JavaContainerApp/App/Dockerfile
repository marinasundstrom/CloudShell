FROM maven:3.9.9-eclipse-temurin-21 AS build

WORKDIR /src
COPY sdk/java/cloudshell ./sdk/java/cloudshell
COPY samples/JavaContainerApp/App ./samples/JavaContainerApp/App
RUN mvn -B -f samples/JavaContainerApp/App/pom.xml -DskipTests package

FROM eclipse-temurin:21-jre

WORKDIR /app
COPY --from=build /src/samples/JavaContainerApp/App/target/cloudshell-java-container-app-sample.jar /app/app.jar
EXPOSE 8080

ENTRYPOINT ["java", "-jar", "/app/app.jar"]

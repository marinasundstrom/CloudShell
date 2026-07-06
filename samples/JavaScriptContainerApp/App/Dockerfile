FROM node:22-alpine

WORKDIR /src
COPY sdk/typescript/configuration-client ./sdk/typescript/configuration-client
COPY samples/JavaScriptContainerApp/App ./samples/JavaScriptContainerApp/App

WORKDIR /src/samples/JavaScriptContainerApp/App
RUN npm ci --omit=dev

ENV HOST=0.0.0.0
ENV PORT=8080
EXPOSE 8080

CMD ["npm", "run", "start"]

FROM node:22-alpine

WORKDIR /app
COPY package.json server.js ./

ENV HOST=0.0.0.0
ENV PORT=8080
EXPOSE 8080

CMD ["npm", "run", "start"]

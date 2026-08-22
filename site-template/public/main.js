const initializeCarousel = () => {
  const carousel = document.querySelector('[data-carousel]');
  if (!carousel) return;

  const slides = [...carousel.querySelectorAll('[data-carousel-slide]')];
  const dots = [...carousel.querySelectorAll('[data-carousel-dot]')];
  const count = carousel.querySelector('[data-carousel-count]');
  const title = carousel.querySelector('[data-carousel-title]');
  const copy = carousel.querySelector('[data-carousel-copy]');
  let current = 0;

  const show = (index) => {
    current = (index + slides.length) % slides.length;
    slides.forEach((slide, slideIndex) => slide.classList.toggle('is-active', slideIndex === current));
    dots.forEach((dot, dotIndex) => {
      const active = dotIndex === current;
      dot.classList.toggle('is-active', active);
      dot.setAttribute('aria-selected', active ? 'true' : 'false');
      dot.tabIndex = active ? 0 : -1;
    });
    count.textContent = `${String(current + 1).padStart(2, '0')} / ${String(slides.length).padStart(2, '0')}`;
    title.textContent = slides[current].dataset.title;
    copy.textContent = slides[current].dataset.copy;
  };

  carousel.querySelector('[data-carousel-prev]').addEventListener('click', () => show(current - 1));
  carousel.querySelector('[data-carousel-next]').addEventListener('click', () => show(current + 1));
  dots.forEach((dot, index) => dot.addEventListener('click', () => show(index)));
  carousel.addEventListener('keydown', (event) => {
    if (event.key === 'ArrowLeft') show(current - 1);
    if (event.key === 'ArrowRight') show(current + 1);
  });
  show(0);
};

export default {
  defaultTheme: 'light',
  iconLinks: [
    {
      icon: 'github',
      href: 'https://github.com/marinasundstrom/CloudShell',
      title: 'CloudShell on GitHub'
    }
  ],
  start: initializeCarousel
};

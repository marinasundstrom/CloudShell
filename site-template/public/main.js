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

const initializePrimaryNavigationLinks = () => {
  const destinations = new Map([
    ['Get started', 'get-started.html'],
    ['Concepts', 'concepts.html']
  ]);

  const enhance = () => {
    const relativeRoot = document.querySelector('meta[name="docfx:rel"]')?.content ?? '';

    document.querySelectorAll('#navbar .navbar-nav > .nav-item.dropdown').forEach((item) => {
      const toggle = item.querySelector(':scope > .dropdown-toggle');
      const label = toggle?.textContent?.replace(/\s+/g, ' ').trim();
      const href = destinations.get(label);
      if (!toggle || !href || item.querySelector(':scope > .cs-nav-parent-link')) return;

      const link = document.createElement('a');
      link.className = 'nav-link cs-nav-parent-link';
      link.href = `${relativeRoot}${href}`;
      link.textContent = label;

      if (toggle.classList.contains('active')) {
        link.classList.add('active');
        toggle.classList.remove('active');
      }

      if (new URL(link.href, window.location.href).pathname === window.location.pathname) {
        link.classList.add('active');
        link.setAttribute('aria-current', 'page');
      }

      toggle.classList.add('cs-nav-menu-toggle');
      toggle.setAttribute('aria-label', `Open ${label} menu`);
      toggle.innerHTML = `<span class="visually-hidden">Open ${label} menu</span>`;
      item.insertBefore(link, toggle);
    });
  };

  const navbar = document.querySelector('#navbar');
  if (!navbar) return;

  enhance();
  new MutationObserver(enhance).observe(navbar, { childList: true, subtree: true });
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
  start: () => {
    initializePrimaryNavigationLinks();
    initializeCarousel();
  }
};
